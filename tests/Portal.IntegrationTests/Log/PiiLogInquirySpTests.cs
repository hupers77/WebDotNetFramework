using Portal.Core.Errors;
using Portal.Core.Results;
using Portal.IntegrationTests.Infrastructure;
using Portal.Web.Areas.Log.Models;
using Portal.Web.Areas.Log.Services;

namespace Portal.IntegrationTests.Log;

/// <summary>
/// 개인정보 접속기록 조회 SP: 사용자·화면·작업 조건, 최근 순, 데이터 범위(전체가 아니면 범위 안 계정만), 기간 검사(31일까지).
/// </summary>
/// <remarks>SP 오류는 테스트 트랜잭션도 롤백하므로 오류 확인은 각 테스트의 마지막 호출로 둔다.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class PiiLogInquirySpTests(DbFixture fixture)
{
    private const string Auditor = "it.pl.auditor";
    private const string Emp = "it.pl.emp";
    private const string Ptn = "it.pl.ptn";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>본사·협력사 계정이 개인정보를 조회·다운로드한 기록 3건을 남기고 보안 감사자(데이터 범위 전체)로 바꾼다.</summary>
    private static async Task<(TestDb Db, DateTime From)> BeginAsync(DbFixture fixture)
    {
        var db = await fixture.BeginAsync();
        await db.SeedCompanyAsync("ITPLPTN");
        await db.SeedUserAsync(Auditor, "HQ", "EMP", null, "SECURITY_AUDITOR");
        await db.SeedUserAsync(Emp, "HQ", "EMP", null, "EMP_USER");
        await db.SeedUserAsync(Ptn, "ITPLPTN", "PARTNER", null, "PARTNER_USER");
        var from = await db.ScalarAsync<DateTime>("SELECT DATEADD(SECOND, -1, SYSUTCDATETIME())");
        db.AsUser(Emp, "HQ", "EMP");
        await WriteAsync(db, "SYS_USER_MGT", "VIEW", null);
        await WriteAsync(db, "SYS_USER_MGT", "DOWNLOAD", "점검");
        db.AsUser(Ptn, "ITPLPTN", "PARTNER");
        await WriteAsync(db, "PTN_USER_MGT", "DOWNLOAD", "정산");
        db.AsUser(Auditor, "HQ", "EMP");
        return (db, from);
    }

    private static Task<Portal.Data.SaveResult> WriteAsync(TestDb db, string programId, string action, string? reason) =>
        db.Sp.ExecuteAsync("LOG.USP_PII_ACCESS_LOG_WRITE", new { P_PROGRAM_ID = programId, P_ACTION_CD = action, P_TARGET_DESC = """{"cnt":1}""", P_REASON = reason }, Ct);

    private static Task<PageResult<PiiLogDto>> ListAsync(TestDb db, DateTime from, string? userId = null, string? programId = null, string? action = null) =>
        db.Sp.QueryPageAsync<PiiLogDto>(PiiLogService.ListSp, new
        {
            P_FROM_DT = from,
            P_TO_DT = from.AddHours(1),
            P_TARGET_USER_ID = userId,
            P_PROGRAM_ID = programId,
            P_ACTION_CD = action,
            P_PAGE_NO = 1,
            P_PAGE_SIZE = 50,
        }, Ct);

    private static string[] Ours(PageResult<PiiLogDto> page) =>
        [.. page.Items.Where(r => r.UserId.StartsWith("it.pl.", StringComparison.Ordinal)).Select(r => $"{r.UserId}:{r.ProgramId}:{r.ActionCd}").Order(StringComparer.Ordinal)];

    [Fact]
    public async Task 조건으로거르고_최근순()
    {
        var (db, from) = await BeginAsync(fixture);
        await using var _ = db;

        var all = await ListAsync(db, from);

        Assert.Equal([$"{Emp}:SYS_USER_MGT:DOWNLOAD", $"{Emp}:SYS_USER_MGT:VIEW", $"{Ptn}:PTN_USER_MGT:DOWNLOAD"], Ours(all));
        Assert.Equal((Ptn, "정산"), (all.Items[0].UserId, all.Items[0].Reason)); // 마지막에 남긴 기록이 먼저
        Assert.Equal([$"{Emp}:SYS_USER_MGT:DOWNLOAD", $"{Ptn}:PTN_USER_MGT:DOWNLOAD"], Ours(await ListAsync(db, from, action: "DOWNLOAD")));
        Assert.Equal([$"{Emp}:SYS_USER_MGT:DOWNLOAD", $"{Emp}:SYS_USER_MGT:VIEW"], Ours(await ListAsync(db, from, userId: Emp)));
        Assert.Equal([$"{Ptn}:PTN_USER_MGT:DOWNLOAD"], Ours(await ListAsync(db, from, programId: "PTN_USER_MGT")));
    }

    [Fact]
    public async Task 데이터범위가전체가아니면_범위안계정기록만()
    {
        var (db, from) = await BeginAsync(fixture);
        await using var _ = db;
        await db.ExecAsync("""
            INSERT INTO FW.TB_ROLE (ROLE_ID, ROLE_NM, COMPANY_TYPE_CD, DATA_SCOPE_CD, SORT_NO) VALUES ('IT_PL_HQ', N'본사 범위', 'HQ', 'COMPANY', 900);
            INSERT INTO FW.TB_ROLE_PROGRAM_FUNC (ROLE_ID, PROGRAM_ID, FUNC_CD) VALUES ('IT_PL_HQ', 'LOG_PII_INQ', 'READ');
            """);
        await db.SeedUserAsync("it.pl.narrow", "HQ", "EMP", null, "IT_PL_HQ");
        db.AsUser("it.pl.narrow", "HQ", "EMP");

        Assert.Equal([$"{Emp}:SYS_USER_MGT:DOWNLOAD", $"{Emp}:SYS_USER_MGT:VIEW"], Ours(await ListAsync(db, from)));
    }

    [Theory]
    [InlineData(0, 0)] // 시작 = 끝
    [InlineData(0, 32)] // 31일 넘음
    public async Task 기간이잘못되면_거부(int fromDays, int toDays)
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedUserAsync(Auditor, "HQ", "EMP", null, "SECURITY_AUDITOR");
        db.AsUser(Auditor, "HQ", "EMP");
        var day = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        var ex = await Assert.ThrowsAsync<BizException>(() =>
            db.Sp.QueryPageAsync<PiiLogDto>(PiiLogService.ListSp, new { P_FROM_DT = day.AddDays(fromDays), P_TO_DT = day.AddDays(toDays), P_PAGE_NO = 1, P_PAGE_SIZE = 50 }, Ct));

        Assert.Equal(LogMessageKeys.Period, ex.MessageKey);
    }
}
