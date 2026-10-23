using Portal.Core.Errors;
using Portal.IntegrationTests.Infrastructure;
using Portal.Web.Areas.Rpt.Models;
using Portal.Web.Areas.Rpt.Services;

namespace Portal.IntegrationTests.Fw;

/// <summary>
/// 레포트 백그라운드 생성 SP: 요청(요청자 회사·유형은 사용자 표에서, 진행 중 5건 한도, 사용 안 하는 레포트 거부), 꺼내기(오래된 순, 멈춘 요청은 실패 처리),
/// 끝내기(포털 알림 완료/실패, 조건 값 지움, 보관 7일, 지운 요청은 알림 없음), 내 요청만 보기·내려받기·지우기, 정리 대상.
/// </summary>
/// <remarks>SP 오류는 테스트 트랜잭션도 롤백하므로 오류 확인은 각 테스트의 마지막 호출로 둔다.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class ReportRunSpTests(DbFixture fixture)
{
    private const string Report = "IT_RPT_R";
    private const string UserA = "it.rr.a";
    private const string UserB = "it.rr.b";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<TestDb> BeginAsync(DbFixture fixture)
    {
        var db = await fixture.BeginAsync();
        await db.ExecAsync("""
            INSERT INTO FW.TB_PROGRAM (PROGRAM_ID, MODULE_CD, PROGRAM_NM, URL) VALUES ('IT_RPT_R', 'RPT', N'시험 레포트', '/Rpt/Viewer/Index/IT_RPT_R');
            INSERT INTO FW.TB_REPORT (REPORT_ID, DATA_SP, MAX_ROWS) VALUES ('IT_RPT_R', 'LOG.USP_RPT_ACCESS_MONTHLY_GET_LIST', 20000);
            """);
        await db.SeedUserAsync(UserA, "HQ", "EMP");
        await db.SeedUserAsync(UserB, "HQ", "EMP");
        db.AsUser(UserA, "HQ", "EMP");
        return db;
    }

    private static async Task<long> QueueAsync(TestDb db, string format = "PDF", string? reason = null)
    {
        var saved = await db.Sp.ExecuteAsync(ReportRunService.SaveSp, new { P_REPORT_ID = Report, P_FORMAT_CD = format, P_PARAMS_JSON = """{"P_MONTH":"2026-09"}""", P_REASON = reason }, Ct);
        return long.Parse(saved.NewKey!, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Task<ClaimedReportRun?> ClaimAsync(TestDb db) =>
        db.Sp.QuerySingleAsync<ClaimedReportRun>(ReportRunProcessor.ClaimSp, null, Ct);

    private static Task<Portal.Data.SaveResult> EndAsync(TestDb db, long runId, string status = "DONE") =>
        db.Sp.ExecuteAsync(ReportRunProcessor.EndSp, status == "DONE"
            ? new { P_RUN_ID = runId, P_STATUS_CD = status, P_ROW_CNT = (int?)3, P_FILE_NM = (string?)"IT_RPT_R-1.pdf", P_FILE_SIZE = (long?)10, P_STORED_PATH = (string?)"rpt/2026/10/x", P_ERR_KEY = (string?)null, P_ERR_ARG = (string?)null }
            : new { P_RUN_ID = runId, P_STATUS_CD = status, P_ROW_CNT = (int?)null, P_FILE_NM = (string?)null, P_FILE_SIZE = (long?)null, P_STORED_PATH = (string?)null, P_ERR_KEY = (string?)"RPT.TOO_MANY", P_ERR_ARG = (string?)"20000" },
            Ct);

    private static Task<string> StatusAsync(TestDb db, long runId) =>
        db.ScalarAsync<string>("SELECT STATUS_CD FROM FW.TB_REPORT_RUN WHERE RUN_ID = @R", ("@R", runId));

    [Fact]
    public async Task 요청_요청자의_회사와유형은_사용자표에서_읽고_대기로쌓인다()
    {
        await using var db = await BeginAsync(fixture);

        var runId = await QueueAsync(db, "EXCEL", "점검");

        Assert.Equal("QUEUED|EXCEL|HQ|EMP|it.rr.a|점검", await db.ScalarAsync<string>(
            "SELECT CONCAT(STATUS_CD, '|', FORMAT_CD, '|', REQ_COMPANY_CD, '|', REQ_USER_TYPE_CD, '|', REQ_USER_ID, '|', REASON) FROM FW.TB_REPORT_RUN WHERE RUN_ID = @R", ("@R", runId)));
    }

    [Fact]
    public async Task 꺼내기_오래된요청부터_RUNNING으로_바꾸고_더없으면_없다()
    {
        await using var db = await BeginAsync(fixture);
        var first = await QueueAsync(db);
        var second = await QueueAsync(db, "EXCEL");

        var a = await ClaimAsync(db);
        var b = await ClaimAsync(db);
        var none = await ClaimAsync(db);

        Assert.Equal((first, "PDF", UserA, "HQ", "EMP"), (a!.RunId, a.FormatCd, a.ReqUserId, a.ReqCompanyCd, a.ReqUserTypeCd));
        Assert.Equal(second, b!.RunId);
        Assert.Null(none);
        Assert.Equal("RUNNING", await StatusAsync(db, first));
    }

    [Fact]
    public async Task 꺼내기_30분_넘게_RUNNING인_요청은_멈춘것으로_실패처리()
    {
        await using var db = await BeginAsync(fixture);
        var runId = await QueueAsync(db);
        await ClaimAsync(db);
        await db.ExecAsync("UPDATE FW.TB_REPORT_RUN SET START_DT = DATEADD(MINUTE, -31, SYSUTCDATETIME()) WHERE RUN_ID = @R", ("@R", runId));

        await ClaimAsync(db);

        Assert.Equal("FAILED|RPT.RUN_INTERRUPTED", await db.ScalarAsync<string>("SELECT CONCAT(STATUS_CD, '|', ERR_KEY) FROM FW.TB_REPORT_RUN WHERE RUN_ID = @R", ("@R", runId)));
    }

    [Fact]
    public async Task 끝내기_완료는_알림과함께_조건지움_보관7일_한번만()
    {
        await using var db = await BeginAsync(fixture);
        var runId = await QueueAsync(db, reason: "점검");
        await ClaimAsync(db);

        var done = await EndAsync(db, runId);
        var again = await EndAsync(db, runId);

        Assert.Equal((1, 0), (done.AffectedCnt, again.AffectedCnt));
        Assert.Equal("DONE|3|IT_RPT_R-1.pdf|{}|7", await db.ScalarAsync<string>(
            "SELECT CONCAT(STATUS_CD, '|', ROW_CNT, '|', FILE_NM, '|', PARAMS_JSON, '|', DATEDIFF(DAY, END_DT, EXPIRE_DT)) FROM FW.TB_REPORT_RUN WHERE RUN_ID = @R", ("@R", runId)));
        Assert.Null(await db.ScalarAsync<string?>("SELECT REASON FROM FW.TB_REPORT_RUN WHERE RUN_ID = @R", ("@R", runId)));
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_NOTIFICATION WHERE USER_ID = @U AND TEMPLATE_ID = 'TPL_REPORT_DONE' AND JSON_VALUE(PARAMS_JSON, '$.REPORT_ID') = @P", ("@U", UserA), ("@P", Report)));
    }

    [Fact]
    public async Task 끝내기_실패는_사유키와_실패알림_지운요청은_알림없음()
    {
        await using var db = await BeginAsync(fixture);
        var failed = await QueueAsync(db);
        var deleted = await QueueAsync(db);
        await ClaimAsync(db);
        await ClaimAsync(db);
        await db.Sp.ExecuteAsync(ReportService_DeleteSp, new { P_RUN_ID = deleted }, Ct);

        await EndAsync(db, failed, "FAILED");
        await EndAsync(db, deleted);

        Assert.Equal("FAILED|RPT.TOO_MANY|20000", await db.ScalarAsync<string>("SELECT CONCAT(STATUS_CD, '|', ERR_KEY, '|', ERR_ARG) FROM FW.TB_REPORT_RUN WHERE RUN_ID = @R", ("@R", failed)));
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_NOTIFICATION WHERE USER_ID = @U AND TEMPLATE_ID = 'TPL_REPORT_FAILED'", ("@U", UserA)));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_NOTIFICATION WHERE USER_ID = @U AND TEMPLATE_ID = 'TPL_REPORT_DONE'", ("@U", UserA)));
    }

    private const string ReportService_DeleteSp = ReportRunService.DeleteSp;

    [Fact]
    public async Task 목록_내요청만_지운요청은_안보임_받기는_저장위치포함()
    {
        await using var db = await BeginAsync(fixture);
        var mine = await QueueAsync(db);
        var gone = await QueueAsync(db);
        await ClaimAsync(db);
        await EndAsync(db, mine);
        await db.Sp.ExecuteAsync(ReportService_DeleteSp, new { P_RUN_ID = gone }, Ct);
        db.AsUser(UserB, "HQ", "EMP");
        var othersList = await db.Sp.QueryAsync<ReportRunItem>(ReportRunService.ListSp, null, Ct);
        var othersGet = await db.Sp.QuerySingleAsync<ReportRunFile>(ReportRunService.GetSp, new { P_RUN_ID = mine }, Ct);
        var othersDelete = await db.Sp.ExecuteAsync(ReportService_DeleteSp, new { P_RUN_ID = mine }, Ct);
        db.AsUser(UserA, "HQ", "EMP");

        var list = await db.Sp.QueryAsync<ReportRunItem>(ReportRunService.ListSp, null, Ct);
        var file = await db.Sp.QuerySingleAsync<ReportRunFile>(ReportRunService.GetSp, new { P_RUN_ID = mine }, Ct);

        Assert.Empty(othersList);
        Assert.Null(othersGet);
        Assert.Equal(0, othersDelete.AffectedCnt);
        Assert.Equal(mine, Assert.Single(list).RunId);
        Assert.Equal(("DONE", "rpt/2026/10/x"), (file!.StatusCd, file.StoredPath));
        Assert.Null(await db.Sp.QuerySingleAsync<ReportRunFile>(ReportRunService.GetSp, new { P_RUN_ID = gone }, Ct));
    }

    [Fact]
    public async Task 정리_보관기간이_지난것만_만드는중은_제외_행을_지운다()
    {
        await using var db = await BeginAsync(fixture);
        var expired = await QueueAsync(db);
        var running = await QueueAsync(db);
        var fresh = await QueueAsync(db);
        await db.ExecAsync("UPDATE FW.TB_REPORT_RUN SET STATUS_CD = 'DONE', STORED_PATH = 'rpt/2026/10/x', EXPIRE_DT = DATEADD(HOUR, -1, SYSUTCDATETIME()) WHERE RUN_ID = @R", ("@R", expired));
        await db.ExecAsync("UPDATE FW.TB_REPORT_RUN SET STATUS_CD = 'RUNNING', START_DT = SYSUTCDATETIME(), EXPIRE_DT = DATEADD(HOUR, -1, SYSUTCDATETIME()) WHERE RUN_ID = @R", ("@R", running));

        var targets = await db.Sp.QueryAsync<ReportRunProcessor.CleanupTarget>(ReportRunProcessor.CleanupListSp, new { P_BATCH_SIZE = 100 }, Ct);
        var deleted = await db.Sp.ExecuteAsync(ReportRunProcessor.CleanupDeleteSp, new { P_RUN_ID = expired }, Ct);
        var notExpired = await db.Sp.ExecuteAsync(ReportRunProcessor.CleanupDeleteSp, new { P_RUN_ID = fresh }, Ct);

        var target = Assert.Single(targets);
        Assert.Equal((expired, "rpt/2026/10/x"), (target.RunId, target.StoredPath));
        Assert.Equal((1, 0), (deleted.AffectedCnt, notExpired.AffectedCnt));
    }

    [Fact]
    public async Task 요청_사용안하는레포트는_없음()
    {
        await using var db = await BeginAsync(fixture);
        await db.ExecAsync("UPDATE FW.TB_REPORT SET USE_YN = 'N' WHERE REPORT_ID = 'IT_RPT_R'");

        var ex = await Assert.ThrowsAsync<BizException>(() => QueueAsync(db));

        Assert.Equal("MSG.NOT_FOUND", ex.MessageKey);
    }

    [Fact]
    public async Task 요청_진행중_5건이면_한도초과()
    {
        await using var db = await BeginAsync(fixture);
        for (var i = 0; i < 5; i++)
        {
            await QueueAsync(db);
        }

        var ex = await Assert.ThrowsAsync<BizException>(() => QueueAsync(db));

        Assert.Equal(ReportMessageKeys.RunLimit, ex.MessageKey);
    }
}
