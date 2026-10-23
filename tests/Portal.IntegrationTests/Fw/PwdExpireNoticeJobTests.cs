using Portal.IntegrationTests.Infrastructure;

namespace Portal.IntegrationTests.Fw;

/// <summary>
/// 비밀번호 만료 안내 작업 SP(<c>FW.USP_JOB_PWD_EXPIRE_NOTICE_EXEC</c>): 만료 7일 전 메일·포털 알림을 변경 주기마다 한 번,
/// 비밀번호를 바꾸면 다음 주기에 다시, 이미 만료됐거나 변경이 강제된 계정·AD 전용·시스템 계정은 제외.
/// </summary>
/// <remarks>기본값 PWD.EXPIRE_DAYS = 90, PWD.EXPIRE_NOTICE_DAYS = 7.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class PwdExpireNoticeJobTests(DbFixture fixture)
{
    private const string Sp = "FW.USP_JOB_PWD_EXPIRE_NOTICE_EXEC";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<TestDb> BeginAsync(DbFixture fixture)
    {
        var db = await fixture.BeginAsync();
        await db.ExecAsync("UPDATE FW.TB_CONFIG SET CONFIG_VAL = '90' WHERE CONFIG_KEY = 'PWD.EXPIRE_DAYS'; UPDATE FW.TB_CONFIG SET CONFIG_VAL = '7' WHERE CONFIG_KEY = 'PWD.EXPIRE_NOTICE_DAYS';");
        return db;
    }

    /// <summary>비밀번호가 있는 계정을 만들고 마지막 변경 시각을 며칠 전으로 맞춘다.</summary>
    private static async Task SeedAsync(TestDb db, string userId, double changedDaysAgo, string type = "EMP", bool hasPwd = true, string chgReq = "N")
    {
        await db.SeedUserAsync(userId, "HQ", type, null, "EMP_USER");
        await db.ExecAsync("""
            UPDATE FW.TB_USER
               SET EMAIL_ENC = 0x01, PWD_HASH = IIF(@P = 1, 0x01, NULL), PWD_SALT = IIF(@P = 1, 0x01, NULL), PWD_CHG_REQ_YN = @R,
                   PWD_CHG_DT = DATEADD(MINUTE, -CAST(@D * 1440 AS INT), SYSUTCDATETIME())
             WHERE USER_ID = @U
            """, ("@U", userId), ("@P", hasPwd ? 1 : 0), ("@R", chgReq), ("@D", changedDaysAgo));
    }

    private static Task<string> CountsAsync(TestDb db, string userId) =>
        db.ScalarAsync<string>("""
            SELECT CONCAT((SELECT COUNT(*) FROM FW.TB_MSG_QUEUE WHERE TEMPLATE_ID = 'TPL_PWD_EXPIRE' AND TO_USER_ID = @U), '|',
                          (SELECT COUNT(*) FROM FW.TB_NOTIFICATION WHERE TEMPLATE_ID = 'TPL_PWD_EXPIRE' AND USER_ID = @U))
            """, ("@U", userId));

    private static Task<ProcessedRow?> RunAsync(TestDb db) => db.Sp.QuerySingleAsync<ProcessedRow>(Sp, null, Ct);

    [Fact]
    public async Task 만료7일안이면_메일과포털알림을한번만보낸다()
    {
        await using var db = await BeginAsync(fixture);
        await SeedAsync(db, "it.pe.soon", changedDaysAgo: 84);   // 6일 남음
        await SeedAsync(db, "it.pe.later", changedDaysAgo: 80);  // 10일 남음

        var first = await RunAsync(db);
        await RunAsync(db); // 다시 돌아도 한 번만

        Assert.Equal(1, first!.ProcessedCnt);
        Assert.Equal("1|1", await CountsAsync(db, "it.pe.soon"));
        Assert.Equal("0|0", await CountsAsync(db, "it.pe.later"));
        Assert.Contains("\"DAYS_LEFT\":\"6\"", await db.ScalarAsync<string>(
            "SELECT PARAMS_JSON FROM FW.TB_MSG_QUEUE WHERE TEMPLATE_ID = 'TPL_PWD_EXPIRE' AND TO_USER_ID = 'it.pe.soon'"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 비밀번호를바꾸면_다음주기에다시안내한다()
    {
        await using var db = await BeginAsync(fixture);
        await SeedAsync(db, "it.pe.cycle", changedDaysAgo: 85);
        await RunAsync(db);

        // 비밀번호를 바꾸고 다시 만료 6일 전이 됨 (안내 시각도 그만큼 과거로)
        await db.ExecAsync("UPDATE FW.TB_USER SET PWD_EXPIRE_NOTICE_DT = DATEADD(DAY, -90, PWD_EXPIRE_NOTICE_DT), PWD_CHG_DT = DATEADD(DAY, -84, SYSUTCDATETIME()) WHERE USER_ID = 'it.pe.cycle'");
        await RunAsync(db);

        Assert.Equal("2|2", await CountsAsync(db, "it.pe.cycle"));
    }

    [Fact]
    public async Task 이미만료_변경강제_AD전용_시스템계정은_안내하지않는다()
    {
        await using var db = await BeginAsync(fixture);
        await SeedAsync(db, "it.pe.expired", changedDaysAgo: 95);
        await SeedAsync(db, "it.pe.forced", changedDaysAgo: 85, chgReq: "Y");
        await SeedAsync(db, "it.pe.ad", changedDaysAgo: 85, hasPwd: false);
        await SeedAsync(db, "it.pe.system", changedDaysAgo: 85, type: "SYSTEM");

        var result = await RunAsync(db);

        Assert.Equal(0, result!.ProcessedCnt);
    }

    [Fact]
    public async Task 만료주기나안내일수가0이면_아무것도하지않는다()
    {
        await using var db = await BeginAsync(fixture);
        await SeedAsync(db, "it.pe.off", changedDaysAgo: 85);

        await db.ExecAsync("UPDATE FW.TB_CONFIG SET CONFIG_VAL = '0' WHERE CONFIG_KEY = 'PWD.EXPIRE_NOTICE_DAYS'");
        await RunAsync(db);
        await db.ExecAsync("UPDATE FW.TB_CONFIG SET CONFIG_VAL = '7' WHERE CONFIG_KEY = 'PWD.EXPIRE_NOTICE_DAYS'; UPDATE FW.TB_CONFIG SET CONFIG_VAL = '0' WHERE CONFIG_KEY = 'PWD.EXPIRE_DAYS';");
        await RunAsync(db);

        Assert.Equal("0|0", await CountsAsync(db, "it.pe.off"));
    }

    private sealed record ProcessedRow(int ProcessedCnt);
}
