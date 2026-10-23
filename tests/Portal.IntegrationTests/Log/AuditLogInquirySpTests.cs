using Portal.Core.Errors;
using Portal.Core.Results;
using Portal.IntegrationTests.Infrastructure;
using Portal.Web.Areas.Log.Models;
using Portal.Web.Areas.Log.Services;

namespace Portal.IntegrationTests.Log;

/// <summary>
/// 데이터 변경 기록 조회 SP: 테이블·작업·대상 키(앞부분, 특수 문자는 글자 그대로)·변경자 조건, 변경 전후 값은 내보내기 때만,
/// 한 건 조회, 테이블 선택지, 데이터 범위가 전체가 아니면 아무것도 안 보임, 기간 검사.
/// </summary>
/// <remarks>SP 오류는 테스트 트랜잭션도 롤백하므로 오류 확인은 각 테스트의 마지막 호출로 둔다.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class AuditLogInquirySpTests(DbFixture fixture)
{
    private const string Auditor = "it.al.auditor";
    private const string Writer = "it.al.writer";
    private const string Table = "IT.TB_AUDIT_INQ";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>변경 기록 3건(등록·수정·삭제)을 남기고 보안 감사자(데이터 범위 전체)로 바꾼다.</summary>
    private static async Task<(TestDb Db, DateTime From)> BeginAsync(DbFixture fixture)
    {
        var db = await fixture.BeginAsync();
        await db.SeedUserAsync(Auditor, "HQ", "EMP", null, "SECURITY_AUDITOR");
        await db.SeedUserAsync(Writer, "HQ", "EMP", null, "EMP_USER");
        var from = await db.ScalarAsync<DateTime>("SELECT DATEADD(SECOND, -1, SYSUTCDATETIME())");
        db.AsUser(Writer, "HQ", "EMP");
        await WriteAsync(db, "I", "k_1", null, """{"NM":"가"}""");
        await WriteAsync(db, "U", "k%2", """{"NM":"가","USE_YN":"Y"}""", """{"NM":"나","USE_YN":"Y"}""");
        await WriteAsync(db, "D", "kx3", """{"NM":"다"}""", null);
        db.AsUser(Auditor, "HQ", "EMP");
        return (db, from);
    }

    private static Task<Portal.Data.SaveResult> WriteAsync(TestDb db, string action, string key, string? before, string? after) =>
        db.Sp.ExecuteAsync("LOG.USP_AUDIT_WRITE", new { P_TABLE_NM = Table, P_ACTION_CD = action, P_ROW_KEY = key, P_BEFORE_JSON = before, P_AFTER_JSON = after }, Ct);

    private static Task<PageResult<AuditLogDto>> ListAsync(TestDb db, DateTime from, string? action = null, string? key = null, string? userId = null, string json = "N") =>
        db.Sp.QueryPageAsync<AuditLogDto>(AuditLogService.ListSp, new
        {
            P_FROM_DT = from,
            P_TO_DT = from.AddHours(1),
            P_TABLE_NM = Table,
            P_ACTION_CD = action,
            P_ROW_KEY = key,
            P_TARGET_USER_ID = userId,
            P_JSON_YN = json,
            P_PAGE_NO = 1,
            P_PAGE_SIZE = 50,
        }, Ct);

    private static string[] Keys(PageResult<AuditLogDto> page) => [.. page.Items.Select(r => r.RowKey).Order(StringComparer.Ordinal)];

    [Fact]
    public async Task 조건으로거르고_대상키특수문자는글자그대로()
    {
        var (db, from) = await BeginAsync(fixture);
        await using var _ = db;

        var all = await ListAsync(db, from);

        Assert.Equal(["k%2", "k_1", "kx3"], Keys(all));
        Assert.Equal("kx3", all.Items[0].RowKey); // 최근 순
        Assert.Equal(["k%2"], Keys(await ListAsync(db, from, action: "U")));
        Assert.Equal(["k_1"], Keys(await ListAsync(db, from, key: "k_"))); // _ 는 아무 글자가 아니다
        Assert.Equal(["k%2"], Keys(await ListAsync(db, from, key: "k%")));
        Assert.Equal(3, (await ListAsync(db, from, userId: Writer)).TotalCount);
        Assert.Empty((await ListAsync(db, from, userId: Auditor)).Items);
    }

    [Fact]
    public async Task 변경전후값은_내보내기와한건조회에서만()
    {
        var (db, from) = await BeginAsync(fixture);
        await using var _ = db;

        var list = await ListAsync(db, from, action: "U");
        var export = await ListAsync(db, from, action: "U", json: "Y");
        var row = Assert.Single(list.Items);
        var one = await db.Sp.QuerySingleAsync<AuditLogDto>(AuditLogService.GetSp, new { P_LOG_ID = row.LogId, P_LOG_DT = row.LogDt }, Ct);

        Assert.Equal((null, null), (row.BeforeJson, row.AfterJson));
        Assert.Contains("나", Assert.Single(export.Items).AfterJson, StringComparison.Ordinal);
        Assert.NotNull(one);
        Assert.Equal((Writer, "U", "k%2"), (one.UserId, one.ActionCd, one.RowKey));
        Assert.Contains("가", one.BeforeJson, StringComparison.Ordinal);
        Assert.Contains(Table, await db.Sp.QueryAsync<string>(AuditLogService.TableListSp, null, Ct));
    }

    [Fact]
    public async Task 데이터범위가전체가아니면_아무것도안보인다()
    {
        var (db, from) = await BeginAsync(fixture);
        await using var _ = db;
        var logId = (await ListAsync(db, from)).Items[0].LogId;
        var logDt = (await ListAsync(db, from)).Items[0].LogDt;
        await db.SeedCompanyAsync("ITALPTN"); // 다른 회사가 있어야 본사 범위가 전체가 아니다
        await db.ExecAsync("""
            INSERT INTO FW.TB_ROLE (ROLE_ID, ROLE_NM, COMPANY_TYPE_CD, DATA_SCOPE_CD, SORT_NO) VALUES ('IT_AL_HQ', N'본사 범위', 'HQ', 'COMPANY', 900);
            INSERT INTO FW.TB_ROLE_PROGRAM_FUNC (ROLE_ID, PROGRAM_ID, FUNC_CD) VALUES ('IT_AL_HQ', 'LOG_AUDIT_INQ', 'READ');
            """);
        await db.SeedUserAsync("it.al.narrow", "HQ", "EMP", null, "IT_AL_HQ");
        db.AsUser("it.al.narrow", "HQ", "EMP");

        var page = await ListAsync(db, from);

        Assert.Equal((0, 0), (page.Items.Count, page.TotalCount));
        Assert.Null(await db.Sp.QuerySingleAsync<AuditLogDto>(AuditLogService.GetSp, new { P_LOG_ID = logId, P_LOG_DT = logDt }, Ct));
        Assert.Empty(await db.Sp.QueryAsync<string>(AuditLogService.TableListSp, null, Ct));
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
            db.Sp.QueryPageAsync<AuditLogDto>(AuditLogService.ListSp, new { P_FROM_DT = day.AddDays(fromDays), P_TO_DT = day.AddDays(toDays), P_PAGE_NO = 1, P_PAGE_SIZE = 50 }, Ct));

        Assert.Equal(LogMessageKeys.Period, ex.MessageKey);
    }
}
