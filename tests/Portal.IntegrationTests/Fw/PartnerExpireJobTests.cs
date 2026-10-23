using Portal.IntegrationTests.Infrastructure;

namespace Portal.IntegrationTests.Fw;

/// <summary>
/// 협력사 계정 만료 작업 SP(<c>FW.USP_JOB_PARTNER_EXPIRE_EXEC</c>): 기한 7일 전 안내(한 번만, 기한을 늘리면 다시), 기한이 지나면 만료,
/// 기한은 계정 사용 기한과 회사 계약 종료일 중 이른 날, 협력사 계정만 대상, 만료는 감사 로그에 남김.
/// </summary>
/// <remarks>기본값 PARTNER.EXPIRE_NOTICE_DAYS = 7. 날짜는 서버 현지 날짜 기준.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class PartnerExpireJobTests(DbFixture fixture)
{
    private const string Sp = "FW.USP_JOB_PARTNER_EXPIRE_EXEC";
    private const string Company = "ITEXP";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<TestDb> BeginAsync(DbFixture fixture)
    {
        var db = await fixture.BeginAsync();
        await db.ExecAsync("UPDATE FW.TB_CONFIG SET CONFIG_VAL = '7' WHERE CONFIG_KEY = 'PARTNER.EXPIRE_NOTICE_DAYS'");
        await db.SeedCompanyAsync(Company);
        return db;
    }

    /// <summary>계정을 만들고 사용 기한을 오늘로부터 며칠 뒤(음수면 며칠 전)로 맞춘다.</summary>
    private static async Task SeedAsync(TestDb db, string userId, int? endInDays, string type = "PARTNER", string company = Company)
    {
        await db.SeedUserAsync(userId, company, type);
        await db.ExecAsync(
            "UPDATE FW.TB_USER SET EMAIL_ENC = 0x01, ACCOUNT_END_DT = DATEADD(DAY, @D, CAST(GETDATE() AS DATE)) WHERE USER_ID = @U",
            ("@U", userId), ("@D", endInDays));
    }

    private static Task<string> StateAsync(TestDb db, string userId) =>
        db.ScalarAsync<string>("SELECT CONCAT(USER_STAT_CD, '|', IIF(EXPIRE_NOTICE_DT IS NULL, 'no-notice', 'noticed')) FROM FW.TB_USER WHERE USER_ID = @U", ("@U", userId));

    private static Task<int> NoticeMailsAsync(TestDb db, string userId) =>
        db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_MSG_QUEUE WHERE TEMPLATE_ID = 'TPL_ACCOUNT_EXPIRE' AND TO_USER_ID = @U", ("@U", userId));

    private static Task<ProcessedRow?> RunAsync(TestDb db) => db.Sp.QuerySingleAsync<ProcessedRow>(Sp, null, Ct);

    [Fact]
    public async Task 기한7일안이면_한번만안내하고_사용상태유지()
    {
        await using var db = await BeginAsync(fixture);
        await SeedAsync(db, "it.ex.soon", endInDays: 7);
        await SeedAsync(db, "it.ex.today", endInDays: 0);
        await SeedAsync(db, "it.ex.later", endInDays: 8);
        await SeedAsync(db, "it.ex.none", endInDays: null);

        await RunAsync(db);
        await RunAsync(db); // 다시 돌아도 안내는 한 번만

        Assert.Equal(("ACTIVE|noticed", 1), (await StateAsync(db, "it.ex.soon"), await NoticeMailsAsync(db, "it.ex.soon")));
        Assert.Equal(("ACTIVE|noticed", 1), (await StateAsync(db, "it.ex.today"), await NoticeMailsAsync(db, "it.ex.today")));
        Assert.Equal(("ACTIVE|no-notice", 0), (await StateAsync(db, "it.ex.later"), await NoticeMailsAsync(db, "it.ex.later")));
        Assert.Equal(("ACTIVE|no-notice", 0), (await StateAsync(db, "it.ex.none"), await NoticeMailsAsync(db, "it.ex.none")));
        Assert.Contains("\"DAYS_LEFT\":\"7\"", await db.ScalarAsync<string>(
            "SELECT PARAMS_JSON FROM FW.TB_MSG_QUEUE WHERE TEMPLATE_ID = 'TPL_ACCOUNT_EXPIRE' AND TO_USER_ID = 'it.ex.soon'"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 기한을늘리면_새기한으로다시안내한다()
    {
        await using var db = await BeginAsync(fixture);
        await SeedAsync(db, "it.ex.renew", endInDays: 5);
        await RunAsync(db);

        await db.ExecAsync("UPDATE FW.TB_USER SET ACCOUNT_END_DT = DATEADD(DAY, 3, ACCOUNT_END_DT) WHERE USER_ID = 'it.ex.renew'"); // 8일 뒤: 아직 안내 전
        await RunAsync(db);
        Assert.Equal(1, await NoticeMailsAsync(db, "it.ex.renew"));

        // 새 기한의 7일 전이 됨 (안내한 날을 9일 전으로 옮겨 시간이 흐른 것처럼 만든다)
        await db.ExecAsync("UPDATE FW.TB_USER SET EXPIRE_NOTICE_DT = DATEADD(DAY, -9, EXPIRE_NOTICE_DT), ACCOUNT_END_DT = DATEADD(DAY, -1, ACCOUNT_END_DT) WHERE USER_ID = 'it.ex.renew'");
        await RunAsync(db);
        Assert.Equal(2, await NoticeMailsAsync(db, "it.ex.renew"));
    }

    [Fact]
    public async Task 기한이지나면_안내여부와상관없이만료하고_감사로그를남긴다()
    {
        await using var db = await BeginAsync(fixture);
        await SeedAsync(db, "it.ex.past", endInDays: -1);

        var result = await RunAsync(db);

        Assert.Equal(1, result!.ProcessedCnt);
        Assert.Equal("EXPIRED|no-notice", await StateAsync(db, "it.ex.past"));
        Assert.Equal(1, await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_USER' AND ROW_KEY = 'it.ex.past' AND AFTER_JSON LIKE '%EXPIRED%'"));
    }

    [Fact]
    public async Task 회사계약종료일이먼저지나면_계정기한이남아도만료()
    {
        await using var db = await BeginAsync(fixture);
        await SeedAsync(db, "it.ex.contract", endInDays: 100);
        await db.ExecAsync("UPDATE FW.TB_COMPANY SET CONTRACT_END_DT = DATEADD(DAY, -1, CAST(GETDATE() AS DATE)) WHERE COMPANY_CD = @C", ("@C", Company));

        await RunAsync(db);

        Assert.Equal("EXPIRED|no-notice", await StateAsync(db, "it.ex.contract"));
    }

    [Fact]
    public async Task 본사계정이나_사용상태가아닌계정은_건드리지않는다()
    {
        await using var db = await BeginAsync(fixture);
        await SeedAsync(db, "it.ex.emp", endInDays: -1, type: "EMP", company: "HQ");
        await SeedAsync(db, "it.ex.locked", endInDays: -1);
        await db.ExecAsync("UPDATE FW.TB_USER SET USER_STAT_CD = 'LOCKED' WHERE USER_ID = 'it.ex.locked'");

        await RunAsync(db);

        Assert.Equal("ACTIVE|no-notice", await StateAsync(db, "it.ex.emp"));
        Assert.Equal("LOCKED|no-notice", await StateAsync(db, "it.ex.locked"));
    }

    [Fact]
    public async Task 안내일수가0이면_안내는하지않고_만료만한다()
    {
        await using var db = await BeginAsync(fixture);
        await db.ExecAsync("UPDATE FW.TB_CONFIG SET CONFIG_VAL = '0' WHERE CONFIG_KEY = 'PARTNER.EXPIRE_NOTICE_DAYS'");
        await SeedAsync(db, "it.ex.quiet", endInDays: 3);
        await SeedAsync(db, "it.ex.gone", endInDays: -2);

        await RunAsync(db);

        Assert.Equal("ACTIVE|no-notice", await StateAsync(db, "it.ex.quiet"));
        Assert.Equal("EXPIRED|no-notice", await StateAsync(db, "it.ex.gone"));
    }

    private sealed record ProcessedRow(int ProcessedCnt);
}
