using Portal.IntegrationTests.Infrastructure;

namespace Portal.IntegrationTests.Fw;

/// <summary>
/// 휴면 전환 작업 SP(<c>FW.USP_JOB_USER_DORMANT_EXEC</c>): 휴면 기준일 30일 전 안내, 안내 후 30일이 지나야 전환(안내 없이 바로 전환하지 않음),
/// 안내 뒤 로그인하면 전환하지 않음, 휴면 해제 직후 다시 전환하지 않음, 시스템 계정 제외, 전환은 감사 로그에 남김.
/// </summary>
/// <remarks>기본값 USER.DORMANT_DAYS = 365, USER.DORMANT_NOTICE_DAYS = 30.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class UserDormantJobTests(DbFixture fixture)
{
    private const string Sp = "FW.USP_JOB_USER_DORMANT_EXEC";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<TestDb> BeginAsync(DbFixture fixture)
    {
        var db = await fixture.BeginAsync();
        await db.ExecAsync("UPDATE FW.TB_CONFIG SET CONFIG_VAL = '365' WHERE CONFIG_KEY = 'USER.DORMANT_DAYS'; UPDATE FW.TB_CONFIG SET CONFIG_VAL = '30' WHERE CONFIG_KEY = 'USER.DORMANT_NOTICE_DAYS';");
        return db;
    }

    /// <summary>계정을 만들고 마지막 로그인·안내·상태 변경 시각을 오늘로부터 며칠 전으로 맞춘다 (등록은 2년 전).</summary>
    private static async Task SeedAsync(TestDb db, string userId, int? loginDaysAgo, int? noticeDaysAgo = null, int? statChgDaysAgo = null, string type = "EMP")
    {
        await db.SeedUserAsync(userId, "HQ", type, null, "EMP_USER");
        await db.ExecAsync("""
            UPDATE FW.TB_USER
               SET REG_DT = DATEADD(DAY, -730, SYSUTCDATETIME()), EMAIL_ENC = 0x01,
                   LAST_LOGIN_DT = DATEADD(DAY, -@L, SYSUTCDATETIME()),
                   DORMANT_NOTICE_DT = DATEADD(DAY, -@N, SYSUTCDATETIME()),
                   STAT_CHG_DT = DATEADD(DAY, -@S, SYSUTCDATETIME())
             WHERE USER_ID = @U
            """, ("@U", userId), ("@L", loginDaysAgo), ("@N", noticeDaysAgo), ("@S", statChgDaysAgo));
    }

    private static Task<string> StateAsync(TestDb db, string userId) =>
        db.ScalarAsync<string>("SELECT CONCAT(USER_STAT_CD, '|', IIF(DORMANT_NOTICE_DT IS NULL, 'no-notice', 'noticed')) FROM FW.TB_USER WHERE USER_ID = @U", ("@U", userId));

    private static Task<int> NoticeMailsAsync(TestDb db, string userId) =>
        db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_MSG_QUEUE WHERE TEMPLATE_ID = 'TPL_DORMANT_NOTICE' AND TO_USER_ID = @U", ("@U", userId));

    private static Task<ProcessedRow?> RunAsync(TestDb db) => db.Sp.QuerySingleAsync<ProcessedRow>(Sp, null, Ct);

    [Fact]
    public async Task 기준일30일전_안내만하고_사용상태유지()
    {
        await using var db = await BeginAsync(fixture);
        await SeedAsync(db, "it.dm.soon", loginDaysAgo: 340);
        await SeedAsync(db, "it.dm.recent", loginDaysAgo: 10);

        await RunAsync(db);
        await RunAsync(db); // 다시 돌아도 안내는 한 번만

        Assert.Equal(("ACTIVE|noticed", 1), (await StateAsync(db, "it.dm.soon"), await NoticeMailsAsync(db, "it.dm.soon")));
        Assert.Equal(("ACTIVE|no-notice", 0), (await StateAsync(db, "it.dm.recent"), await NoticeMailsAsync(db, "it.dm.recent")));
    }

    [Fact]
    public async Task 안내없이는_바로휴면되지않고_안내후30일이지나면휴면()
    {
        await using var db = await BeginAsync(fixture);
        await SeedAsync(db, "it.dm.never", loginDaysAgo: 500);              // 기간은 지났지만 안내를 못 받음
        await SeedAsync(db, "it.dm.notice", loginDaysAgo: 500, noticeDaysAgo: 31);
        await SeedAsync(db, "it.dm.wait", loginDaysAgo: 500, noticeDaysAgo: 5);

        await RunAsync(db);

        Assert.Equal("ACTIVE|noticed", await StateAsync(db, "it.dm.never"));
        Assert.Equal("DORMANT|no-notice", await StateAsync(db, "it.dm.notice"));
        Assert.Equal("ACTIVE|noticed", await StateAsync(db, "it.dm.wait"));
        Assert.Equal(1, await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_USER' AND ROW_KEY = 'it.dm.notice' AND AFTER_JSON LIKE '%DORMANT%'"));
    }

    [Fact]
    public async Task 안내뒤로그인했거나_휴면해제직후거나_시스템계정이면_휴면하지않는다()
    {
        await using var db = await BeginAsync(fixture);
        await SeedAsync(db, "it.dm.back", loginDaysAgo: 3, noticeDaysAgo: 40);           // 안내 받고 로그인함
        await SeedAsync(db, "it.dm.restored", loginDaysAgo: 500, noticeDaysAgo: null, statChgDaysAgo: 2); // 관리자가 방금 해제
        await SeedAsync(db, "it.dm.system", loginDaysAgo: 900, noticeDaysAgo: 100, type: "SYSTEM");

        await RunAsync(db);

        Assert.Equal("ACTIVE|noticed", await StateAsync(db, "it.dm.back"));
        Assert.Equal("ACTIVE|no-notice", await StateAsync(db, "it.dm.restored"));
        Assert.Equal("ACTIVE|noticed", await StateAsync(db, "it.dm.system"));
        Assert.Equal(0, await NoticeMailsAsync(db, "it.dm.back"));
    }

    [Fact]
    public async Task 휴면일수가0이면_아무것도하지않는다()
    {
        await using var db = await BeginAsync(fixture);
        await db.ExecAsync("UPDATE FW.TB_CONFIG SET CONFIG_VAL = '0' WHERE CONFIG_KEY = 'USER.DORMANT_DAYS'");
        await SeedAsync(db, "it.dm.off", loginDaysAgo: 500, noticeDaysAgo: 100);

        await RunAsync(db);

        Assert.Equal("ACTIVE|noticed", await StateAsync(db, "it.dm.off"));
    }

    private sealed record ProcessedRow(int ProcessedCnt);
}
