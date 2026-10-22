using Portal.Core.Errors;
using Portal.Core.Results;
using Portal.Data;
using Portal.Framework.Web.Logging;
using Portal.IntegrationTests.Infrastructure;
using Portal.Web.Areas.Log.Models;
using Portal.Web.Areas.Log.Services;

namespace Portal.IntegrationTests.Log;

/// <summary>
/// 로그인·접근 기록 조회 SP: 기간·사용자·결과·IP·화면·오류 조건, 쪽 나눔과 전체 건수, 데이터 범위(전체면 없는 ID 기록까지, 아니면 범위 안 계정만),
/// 기간 검사(31일까지).
/// </summary>
/// <remarks>SP 오류는 테스트 트랜잭션도 롤백하므로 오류 확인은 각 테스트의 마지막 호출로 둔다.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class AccessLogInquirySpTests(DbFixture fixture)
{
    private const string Admin = "it.li.admin";
    private const string Emp = "it.li.emp";
    private const string Ptn = "it.li.ptn";
    private const string Ghost = "it.li.ghost";
    private const string Ip = "10.77.0.1";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>본사·협력사 계정과 포털 관리자(현재 사용자)를 만들고, 로그인 기록 3건(성공·비밀번호 오류·없는 ID)을 남긴다.</summary>
    private static async Task<(TestDb Db, DateTime From)> BeginAsync(DbFixture fixture)
    {
        var db = await fixture.BeginAsync();
        await db.SeedCompanyAsync("ITLIPTN");
        await db.SeedUserAsync(Admin, "HQ", "EMP", null, "PORTAL_ADMIN");
        await db.SeedUserAsync(Emp, "HQ", "EMP", null, "EMP_USER");
        await db.SeedUserAsync(Ptn, "ITLIPTN", "PARTNER", null, "PARTNER_USER");
        var from = await db.ScalarAsync<DateTime>("SELECT DATEADD(SECOND, -1, SYSUTCDATETIME())");
        await LoginAsync(db, Emp, "SUCCESS");
        await LoginAsync(db, Ptn, "FAIL_PWD");
        await LoginAsync(db, Ghost, "FAIL_PWD");
        db.AsUser(Admin, "HQ", "EMP");
        return (db, from);
    }

    private static Task<SaveResult> LoginAsync(TestDb db, string userId, string resultCd) =>
        db.Sp.ExecuteAsync("LOG.USP_LOGIN_LOG_WRITE", new { P_LOGIN_USER_ID = userId, P_LOGIN_RESULT_CD = resultCd, P_AUTH_METHOD_CD = "FORM", P_USER_AGENT = "it-agent" }, Ct);

    private static Task<PageResult<LoginLogDto>> LoginsAsync(TestDb db, DateTime from, string? userId = null, string? resultCd = null, string? ip = null, int pageSize = 50) =>
        db.Sp.QueryPageAsync<LoginLogDto>(AccessLogService.LoginListSp, new
        {
            P_FROM_DT = from,
            P_TO_DT = from.AddHours(1),
            P_TARGET_USER_ID = userId,
            P_RESULT_CD = resultCd,
            P_IP = ip,
            P_PAGE_NO = 1,
            P_PAGE_SIZE = pageSize,
        }, Ct);

    private static string[] Ours(IEnumerable<LoginLogDto> rows) => [.. rows.Select(r => r.UserId ?? "").Where(u => u.StartsWith("it.li.", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

    [Fact]
    public async Task 로그인기록_전체범위는없는ID까지_조건으로거른다()
    {
        var (db, from) = await BeginAsync(fixture);
        await using var _ = db;

        var all = await LoginsAsync(db, from);
        var failed = await LoginsAsync(db, from, resultCd: "FAIL_PWD");
        var one = await LoginsAsync(db, from, userId: Emp);

        Assert.Equal([Emp, Ghost, Ptn], Ours(all.Items));
        Assert.Equal([Ghost, Ptn], Ours(failed.Items));
        var emp = Assert.Single(one.Items);
        Assert.Equal(("SUCCESS", "성공", "FORM", "it-agent", 1), (emp.LoginResultCd, emp.LoginResultNm, emp.AuthMethodCd, emp.UserAgent, one.TotalCount));
    }

    [Fact]
    public async Task 로그인기록_최근순_쪽크기와전체건수()
    {
        var (db, from) = await BeginAsync(fixture);
        await using var _ = db;

        var page = await LoginsAsync(db, from, resultCd: "FAIL_PWD", pageSize: 1);

        Assert.Equal((1, 2), (page.Items.Count, page.TotalCount));
        Assert.Equal(Ghost, page.Items[0].UserId); // 마지막에 남긴 기록이 먼저
    }

    [Fact]
    public async Task 데이터범위가전체가아니면_범위안계정기록만()
    {
        var (db, from) = await BeginAsync(fixture);
        await using var _ = db;
        await db.ExecAsync("""
            INSERT INTO FW.TB_ROLE (ROLE_ID, ROLE_NM, COMPANY_TYPE_CD, DATA_SCOPE_CD, SORT_NO) VALUES ('IT_LI_HQ', N'본사 범위', 'HQ', 'COMPANY', 900);
            INSERT INTO FW.TB_ROLE_PROGRAM_FUNC (ROLE_ID, PROGRAM_ID, FUNC_CD) VALUES ('IT_LI_HQ', 'LOG_ACCESS_INQ', 'READ');
            """);
        await db.SeedUserAsync("it.li.narrow", "HQ", "EMP", null, "IT_LI_HQ");
        db.AsUser("it.li.narrow", "HQ", "EMP");

        Assert.Equal("N", await db.ScalarAsync<string>("SELECT ALL_YN FROM FW.UFN_DATA_SCOPE_ALL('it.li.narrow', 'LOG_ACCESS_INQ')"));
        Assert.Equal("Y", await db.ScalarAsync<string>("SELECT ALL_YN FROM FW.UFN_DATA_SCOPE_ALL(@U, 'LOG_ACCESS_INQ')", ("@U", Admin)));
        Assert.Equal([Emp], Ours((await LoginsAsync(db, from)).Items));
    }

    [Fact]
    public async Task 접근기록_화면오류IP조건()
    {
        var (db, from) = await BeginAsync(fixture);
        await using var _ = db;
        var at = from.AddMilliseconds(500);
        await db.Sp.ExecuteAsync("LOG.USP_ACCESS_LOG_WRITE", new
        {
            P_ROWS = Tvp.From("LOG.TVP_ACCESS_LOG", new[]
            {
                Entry(1, at, Emp, "SYS_CODE_MGT", 200),
                Entry(2, at, Emp, "SYS_CODE_MGT", 403),
                Entry(3, at, Ptn, "DSH_HOME", 500),
            }),
        }, Ct);

        async Task<string[]> AccessAsync(string? programId = null, string? errorYn = null) =>
            [.. (await db.Sp.QueryPageAsync<AccessLogDto>(AccessLogService.AccessListSp, new
            {
                P_FROM_DT = from,
                P_TO_DT = from.AddHours(1),
                P_PROGRAM_ID = programId,
                P_ERROR_YN = errorYn,
                P_IP = Ip,
                P_PAGE_NO = 1,
                P_PAGE_SIZE = 50,
            }, Ct)).Items.Select(r => $"{r.UserId}:{r.ProgramId}:{r.StatusCode}").Order(StringComparer.Ordinal)];

        Assert.Equal([$"{Emp}:SYS_CODE_MGT:200", $"{Emp}:SYS_CODE_MGT:403", $"{Ptn}:DSH_HOME:500"], await AccessAsync());
        Assert.Equal([$"{Emp}:SYS_CODE_MGT:403", $"{Ptn}:DSH_HOME:500"], await AccessAsync(errorYn: "Y"));
        Assert.Equal([$"{Emp}:SYS_CODE_MGT:200", $"{Emp}:SYS_CODE_MGT:403"], await AccessAsync(programId: "SYS_CODE_MGT"));
    }

    [Theory]
    [InlineData(0, 0)] // 시작 = 끝
    [InlineData(5, 0)] // 시작이 끝보다 늦음
    [InlineData(0, 32)] // 31일 넘음
    public async Task 기간이잘못되면_거부(int fromDays, int toDays)
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedUserAsync(Admin, "HQ", "EMP", null, "PORTAL_ADMIN");
        db.AsUser(Admin, "HQ", "EMP");
        var day = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        var ex = await Assert.ThrowsAsync<BizException>(() =>
            db.Sp.QueryPageAsync<LoginLogDto>(AccessLogService.LoginListSp, new { P_FROM_DT = day.AddDays(fromDays), P_TO_DT = day.AddDays(toDays), P_PAGE_NO = 1, P_PAGE_SIZE = 50 }, Ct));

        Assert.Equal(LogMessageKeys.Period, ex.MessageKey);
    }

    private static AccessLogEntry Entry(int seq, DateTime at, string userId, string programId, int status) => new()
    {
        SeqNo = seq,
        LogDt = at,
        UserId = userId,
        ProgramId = programId,
        FuncCd = "READ",
        HttpMethod = "GET",
        RequestPath = "/it",
        StatusCode = (short)status,
        ElapsedMs = 5,
        ClientIp = Ip,
        TraceId = "it-li",
    };
}
