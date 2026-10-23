using Portal.Core.Errors;
using Portal.Data;
using Portal.IntegrationTests.Infrastructure;

namespace Portal.IntegrationTests.Log;

/// <summary>
/// 레포트 데이터 SP <c>LOG.USP_RPT_ACCESS_MONTHLY_GET_LIST</c>: 그 달의 날마다 한 행(기록 없는 날도 0), 로그 종류별 건수,
/// 최대 행 수 + 1까지, 데이터 범위가 전체가 아니면 거부, 월 형식 검사.
/// </summary>
/// <remarks>다른 테스트·개발 데이터가 섞이므로 건수는 기록 전후 차이로 확인한다. SP 오류는 트랜잭션도 롤백하므로 오류 확인은 마지막 호출로 둔다.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class AccessMonthlyReportSpTests(DbFixture fixture)
{
    private const string Sp = "LOG.USP_RPT_ACCESS_MONTHLY_GET_LIST";
    private const string Auditor = "it.rm.auditor";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<TestDb> BeginAsync(DbFixture fixture)
    {
        var db = await fixture.BeginAsync();
        await db.SeedUserAsync(Auditor, "HQ", "EMP", null, "SECURITY_AUDITOR");
        db.AsUser(Auditor, "HQ", "EMP");
        return db;
    }

    private static Task<IReadOnlyList<SpRow>> RunAsync(TestDb db, string month, int maxRows = 1000) =>
        db.Sp.QueryAsync<SpRow>(Sp, new { P_MONTH = month, P_MAX_ROWS = maxRows }, Ct);

    private static async Task<SpRow> TodayAsync(TestDb db)
    {
        var today = DateOnly.FromDateTime(await db.ScalarAsync<DateTime>("SELECT CAST(SYSDATETIME() AS DATE)"));
        var rows = await RunAsync(db, today.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture));
        return rows.Single(r => (DateOnly)r["LOG_DATE"]! == today);
    }

    private static int Diff(SpRow before, SpRow after, string column) => (int)after[column]! - (int)before[column]!;

    [Fact]
    public async Task 그달의날마다한행_기록없는날도0_컬럼순서()
    {
        await using var db = await BeginAsync(fixture);

        var feb = await RunAsync(db, "2028-02");

        Assert.Equal(29, feb.Count);
        Assert.Equal(new DateOnly(2028, 2, 1), feb[0]["LOG_DATE"]);
        Assert.Equal(new DateOnly(2028, 2, 29), feb[^1]["LOG_DATE"]);
        Assert.Equal(
            ["LOG_DATE", "LOGIN_OK_CNT", "LOGIN_FAIL_CNT", "LOGIN_LOCK_CNT", "ACCESS_CNT", "ACCESS_DENIED_CNT", "SERVER_ERROR_CNT", "PII_VIEW_CNT", "PII_DOWNLOAD_CNT", "PII_UNMASK_CNT", "AUDIT_CNT"],
            feb[0].Columns);
        Assert.All(feb, r => Assert.All(r.Columns.Skip(1), c => Assert.Equal(0, r[c])));
        Assert.Equal(4, (await RunAsync(db, "2028-02", maxRows: 3)).Count);
    }

    [Fact]
    public async Task 로그종류별건수_오늘행에더해진다()
    {
        await using var db = await BeginAsync(fixture);
        var before = await TodayAsync(db);

        foreach (var result in new[] { "SUCCESS", "FAIL_PWD", "FAIL_LOCKED" })
        {
            await db.Sp.ExecuteAsync("LOG.USP_LOGIN_LOG_WRITE", new { P_LOGIN_USER_ID = Auditor, P_LOGIN_RESULT_CD = result, P_AUTH_METHOD_CD = "FORM" }, Ct);
        }

        foreach (var action in new[] { "VIEW", "DOWNLOAD", "UNMASK" })
        {
            await db.Sp.ExecuteAsync("LOG.USP_PII_ACCESS_LOG_WRITE", new { P_PROGRAM_ID = "SYS_USER_MGT", P_ACTION_CD = action, P_TARGET_DESC = "{}", P_REASON = "점검" }, Ct);
        }

        await db.ExecAsync("""
            DECLARE @T LOG.TVP_ACCESS_LOG;
            INSERT INTO @T (SEQ_NO, LOG_DT, USER_ID, PROGRAM_ID, FUNC_CD, HTTP_METHOD, REQUEST_PATH, STATUS_CODE, ELAPSED_MS, CLIENT_IP, TRACE_ID)
            VALUES (1, SYSUTCDATETIME(), @U, NULL, NULL, 'GET', '/', 200, 1, NULL, NULL),
                   (2, SYSUTCDATETIME(), @U, NULL, NULL, 'GET', '/', 403, 1, NULL, NULL),
                   (3, SYSUTCDATETIME(), @U, NULL, NULL, 'GET', '/', 500, 1, NULL, NULL);
            EXEC LOG.USP_ACCESS_LOG_WRITE @P_ROWS = @T, @P_USER_ID = NULL, @P_LANG_CD = 'ko';
            """, ("@U", Auditor));
        var after = await TodayAsync(db);

        Assert.Equal((1, 2, 1), (Diff(before, after, "LOGIN_OK_CNT"), Diff(before, after, "LOGIN_FAIL_CNT"), Diff(before, after, "LOGIN_LOCK_CNT")));
        Assert.Equal((3, 1, 1), (Diff(before, after, "ACCESS_CNT"), Diff(before, after, "ACCESS_DENIED_CNT"), Diff(before, after, "SERVER_ERROR_CNT")));
        Assert.Equal((1, 1, 1), (Diff(before, after, "PII_VIEW_CNT"), Diff(before, after, "PII_DOWNLOAD_CNT"), Diff(before, after, "PII_UNMASK_CNT")));
    }

    [Fact]
    public async Task 데이터범위가전체가아니면_거부()
    {
        await using var db = await fixture.BeginAsync();
        await db.ExecAsync("""
            INSERT INTO FW.TB_ROLE (ROLE_ID, ROLE_NM, COMPANY_TYPE_CD, DATA_SCOPE_CD, SORT_NO) VALUES ('IT_RM_HQ', N'본사 범위', 'HQ', 'COMPANY', 900);
            INSERT INTO FW.TB_ROLE_PROGRAM_FUNC (ROLE_ID, PROGRAM_ID, FUNC_CD) VALUES ('IT_RM_HQ', 'RPT_LOG_MONTHLY', 'READ');
            """);
        await db.SeedUserAsync("it.rm.narrow", "HQ", "EMP", null, "IT_RM_HQ");
        db.AsUser("it.rm.narrow", "HQ", "EMP");

        var ex = await Assert.ThrowsAsync<BizException>(() => RunAsync(db, "2026-09"));

        Assert.Equal("RPT.SCOPE_ALL_REQUIRED", ex.MessageKey);
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("2026-1")]
    [InlineData("abcd-ef")]
    public async Task 월형식이틀리면_거부(string month)
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => RunAsync(db, month));

        Assert.Equal("MSG.INVALID_INPUT", ex.MessageKey);
    }
}
