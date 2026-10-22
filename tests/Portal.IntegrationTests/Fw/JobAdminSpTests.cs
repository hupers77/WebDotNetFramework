using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.IntegrationTests.Infrastructure;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.IntegrationTests.Fw;

/// <summary>스케줄 관리 SP: 목록·이력, 설정 저장(동시성·다음 시각 초기화·감사 로그), 수동 실행 요청.</summary>
/// <remarks>SP 오류는 테스트 트랜잭션도 롤백하므로 오류 확인은 각 테스트의 마지막 호출로 둔다.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class JobAdminSpTests(DbFixture fixture)
{
    private const string Job = "JOB_IT_ADMIN";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<TestDb> BeginAsync(DbFixture fixture, string useYn = "Y")
    {
        var db = await fixture.BeginAsync();
        await db.ExecAsync(
            "INSERT INTO FW.TB_JOB (JOB_ID, JOB_NM, JOB_TYPE_CD, JOB_TARGET, CRON_EXPR, TIMEOUT_MIN, USE_YN, NEXT_RUN_DT) VALUES (@J, N'관리 테스트', 'SP', 'FW.USP_JOB_DATA_CLEANUP_EXEC', '0 5 * * *', 60, @U, '2030-01-01')",
            ("@J", Job), ("@U", useYn));
        return db;
    }

    private static Task<Portal.Data.SaveResult> SaveAsync(TestDb db, int editVer, string? cron = "0 5 * * *", string useYn = "Y", int timeout = 60, string? desc = null) =>
        db.Sp.ExecuteAsync(JobAdminService.SaveSp, new { P_JOB_ID = Job, P_EDIT_VER = editVer, P_CRON_EXPR = cron, P_TIMEOUT_MIN = timeout, P_USE_YN = useYn, P_DESCRIPTION = desc }, Ct);

    private static Task<string> StateAsync(TestDb db) =>
        db.ScalarAsync<string>(
            "SELECT CONCAT(ISNULL(CRON_EXPR, '-'), '|', TIMEOUT_MIN, '|', USE_YN, '|', ISNULL(DESCRIPTION, '-'), '|', EDIT_VER, '|', IIF(NEXT_RUN_DT IS NULL, 'next-null', 'next-kept'), '|', UPD_ID) FROM FW.TB_JOB WHERE JOB_ID = @J",
            ("@J", Job));

    [Fact]
    public async Task 목록_검색과결과명()
    {
        await using var db = await BeginAsync(fixture);
        await db.ExecAsync("UPDATE FW.TB_JOB SET LAST_RESULT_CD = 'FAILED' WHERE JOB_ID = @J", ("@J", Job));

        var page = await db.Sp.QueryPageAsync<JobDto>(JobAdminService.ListSp, new { P_KEYWORD = "IT_ADMIN", P_PAGE_NO = 1, P_PAGE_SIZE = 50 }, Ct);

        var row = Assert.Single(page.Items);
        Assert.Equal((Job, "실패", 1, "FW.USP_JOB_DATA_CLEANUP_EXEC"), (row.JobId, row.LastResultNm, row.EditVer, row.JobTarget));
    }

    [Fact]
    public async Task 이력_최근것부터_구분명결과명()
    {
        await using var db = await BeginAsync(fixture);
        const string Hist = "INSERT INTO FW.TB_JOB_HIST (JOB_ID, RUN_TYPE_CD, RUN_USER_ID, START_DT, END_DT, RESULT_CD, PROCESSED_CNT) VALUES (@J, @T, @U, DATEADD(MINUTE, @M, SYSUTCDATETIME()), SYSUTCDATETIME(), @R, 3)";
        await db.ExecAsync(Hist, ("@J", Job), ("@T", "SCHEDULE"), ("@U", null), ("@M", -10), ("@R", "SUCCESS"));
        await db.ExecAsync(Hist, ("@J", Job), ("@T", "MANUAL"), ("@U", "admin1"), ("@M", -1), ("@R", "FAILED"));

        var page = await db.Sp.QueryPageAsync<JobHistDto>(JobAdminService.HistorySp, new { P_JOB_ID = Job, P_PAGE_NO = 1, P_PAGE_SIZE = 50 }, Ct);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(["수동|admin1|실패", "예약|-|성공"], page.Items.Select(h => $"{h.RunTypeNm}|{h.RunUserId ?? "-"}|{h.ResultNm}"));
    }

    [Fact]
    public async Task 저장_주기를바꾸면다음시각을비우고_감사로그를남긴다()
    {
        await using var db = await BeginAsync(fixture);
        await SaveAsync(db, 1, timeout: 30, desc: "설명");
        Assert.Equal($"0 5 * * *|30|Y|설명|2|next-kept|{db.User.UserId}", await StateAsync(db)); // 주기가 같으면 다음 시각은 그대로

        await SaveAsync(db, 2, cron: "0 6 * * *", timeout: 30, desc: "설명");
        Assert.Equal($"0 6 * * *|30|Y|설명|3|next-null|{db.User.UserId}", await StateAsync(db));

        var audit = await db.ScalarAsync<string>("SELECT TOP 1 AFTER_JSON FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_JOB' AND ROW_KEY = @J ORDER BY LOG_ID DESC", ("@J", Job));
        Assert.Contains("\"CRON_EXPR\":\"0 6 * * *\"", audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 저장_다시사용으로바꾸면_다음시각을비운다()
    {
        await using var db = await BeginAsync(fixture, useYn: "N");

        await SaveAsync(db, 1);

        Assert.EndsWith("|Y|-|2|next-null|" + db.User.UserId, await StateAsync(db), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 저장_Worker가실행상태를바꿔도_동시성오류가아니다()
    {
        await using var db = await BeginAsync(fixture);
        await db.ExecAsync("UPDATE FW.TB_JOB SET RUNNING_YN = 'Y', LOCK_DT = SYSUTCDATETIME(), NEXT_RUN_DT = '2031-01-01' WHERE JOB_ID = @J", ("@J", Job));

        await SaveAsync(db, 1, timeout: 45);

        Assert.StartsWith("0 5 * * *|45|", await StateAsync(db), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 저장_다른사람이먼저저장했으면_동시성오류()
    {
        await using var db = await BeginAsync(fixture);
        await SaveAsync(db, 1);

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, 1));

        Assert.Equal(MessageKeys.Concurrency, ex.MessageKey);
    }

    [Fact]
    public async Task 수동실행요청_요청자를남기고_감사로그()
    {
        await using var db = await BeginAsync(fixture);

        await db.Sp.ExecuteAsync(JobAdminService.RunSp, new { P_JOB_ID = Job }, Ct);

        Assert.Equal($"Y|{db.User.UserId}", await db.ScalarAsync<string>("SELECT CONCAT(RUN_REQ_YN, '|', RUN_REQ_ID) FROM FW.TB_JOB WHERE JOB_ID = @J", ("@J", Job)));
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_JOB' AND ROW_KEY = @J AND AFTER_JSON LIKE '%RUN_REQ_YN%'", ("@J", Job)));
    }

    [Fact]
    public async Task 수동실행요청_사용안하는작업은거부()
    {
        await using var db = await BeginAsync(fixture, useYn: "N");

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync(JobAdminService.RunSp, new { P_JOB_ID = Job }, Ct));

        Assert.Equal(JobMessageKeys.NotInUse, ex.MessageKey);
    }
}
