using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portal.Core.Scheduling;
using Portal.Data;
using Portal.Infrastructure.Files;
using Portal.Infrastructure.Scheduling;
using Portal.IntegrationTests.Infrastructure;

namespace Portal.IntegrationTests.Fw;

/// <summary>스케줄러 SP: 실행할 작업 찾기, 잠금·넘겨받기, 결과 기록과 실패 알림, 기본 정리 작업.</summary>
/// <remarks>기본 작업(시드)은 테스트마다 꺼 두고 테스트용 작업만 쓴다.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class JobSpTests(DbFixture fixture)
{
    private const string Job = "JOB_IT_TEST";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<TestDb> BeginAsync(DbFixture fixture, string? cron = "*/10 * * * *", string next = "DATEADD(MINUTE, -1, SYSUTCDATETIME())", string type = "CLASS", string target = "Test")
    {
        var db = await fixture.BeginAsync();
        await db.ExecAsync("UPDATE FW.TB_JOB SET USE_YN = 'N'");
        await db.ExecAsync(
            $"INSERT INTO FW.TB_JOB (JOB_ID, JOB_NM, JOB_TYPE_CD, JOB_TARGET, CRON_EXPR, TIMEOUT_MIN, NEXT_RUN_DT) VALUES (@J, N'테스트', @T, @G, @C, 30, {next})",
            ("@J", Job), ("@T", type), ("@G", target), ("@C", cron));
        return db;
    }

    private static Task<string> JobStateAsync(TestDb db) =>
        db.ScalarAsync<string>(
            "SELECT CONCAT(RUNNING_YN, '|', RUN_REQ_YN, '|', ISNULL(LAST_RESULT_CD, '-'), '|', IIF(LOCK_DT IS NULL, 'unlocked', 'locked')) FROM FW.TB_JOB WHERE JOB_ID = @J", ("@J", Job));

    private static Task<string> HistAsync(TestDb db, long histId) =>
        db.ScalarAsync<string>(
            "SELECT CONCAT(RUN_TYPE_CD, '|', ISNULL(RUN_USER_ID, '-'), '|', RESULT_CD, '|', ISNULL(CAST(PROCESSED_CNT AS VARCHAR(10)), '-'), '|', ISNULL(RESULT_MSG, '-'), '|', IIF(END_DT IS NULL, 'open', 'ended'))"
            + " FROM FW.TB_JOB_HIST WHERE HIST_ID = @H", ("@H", histId));

    private static Task<JobScheduler.StartedRun?> StartAsync(TestDb db, DateTime? next = null) =>
        db.Sp.QuerySingleAsync<JobScheduler.StartedRun>(JobScheduler.StartSp, new { P_JOB_ID = Job, P_NEXT_RUN_DT = next, P_HOST_NM = "it-host" }, Ct);

    private static Task<SaveResult> EndAsync(TestDb db, long histId, string result, string? msg = null, int? cnt = null) =>
        db.Sp.ExecuteAsync(JobScheduler.EndSp, new { P_HIST_ID = histId, P_RESULT_CD = result, P_RESULT_MSG = msg, P_PROCESSED_CNT = cnt }, Ct);

    private static async Task<IReadOnlyList<string>> DueIdsAsync(TestDb db) =>
        [.. (await db.Sp.QueryAsync<JobScheduler.DueJob>(JobScheduler.DueSp, null, Ct)).Select(j => j.JobId)];

    [Fact]
    public async Task 기본작업_시드주기가모두올바르다()
    {
        await using var db = await fixture.BeginAsync();

        var crons = await db.ColumnAsync<string>("SELECT CONCAT(JOB_ID, '=', CRON_EXPR) FROM FW.TB_JOB WHERE JOB_ID IN ('JOB_MAIL_SEND', 'JOB_SESSION_CLEANUP', 'JOB_TEMP_CLEANUP', 'JOB_DATA_CLEANUP') ORDER BY JOB_ID");

        Assert.Equal(4, crons.Count);
        Assert.All(crons, c => CronSchedule.Parse(c[(c.IndexOf('=', StringComparison.Ordinal) + 1)..]));
    }

    [Fact]
    public async Task 실행할작업찾기_예약시각지남_수동요청_다음시각미계산()
    {
        await using var db = await BeginAsync(fixture, next: "DATEADD(MINUTE, 5, SYSUTCDATETIME())");
        Assert.Empty(await DueIdsAsync(db));

        await db.ExecAsync("UPDATE FW.TB_JOB SET RUN_REQ_YN = 'Y' WHERE JOB_ID = @J", ("@J", Job));
        Assert.Equal([Job], await DueIdsAsync(db));

        await db.ExecAsync("UPDATE FW.TB_JOB SET RUN_REQ_YN = 'N', NEXT_RUN_DT = NULL WHERE JOB_ID = @J", ("@J", Job));
        Assert.Equal([Job], await DueIdsAsync(db));

        await db.ExecAsync("UPDATE FW.TB_JOB SET CRON_EXPR = NULL WHERE JOB_ID = @J", ("@J", Job));
        Assert.Empty(await DueIdsAsync(db)); // 주기 없는 작업은 수동 요청 때만
    }

    [Fact]
    public async Task 다음시각채우기_비어있을때만()
    {
        await using var db = await BeginAsync(fixture, next: "NULL");
        var first = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(1, (await db.Sp.ExecuteAsync(JobScheduler.NextRunSp, new { P_JOB_ID = Job, P_NEXT_RUN_DT = first }, Ct)).AffectedCnt);
        Assert.Equal(0, (await db.Sp.ExecuteAsync(JobScheduler.NextRunSp, new { P_JOB_ID = Job, P_NEXT_RUN_DT = first.AddDays(1) }, Ct)).AffectedCnt);
        Assert.Equal(first, await db.ScalarAsync<DateTime>("SELECT NEXT_RUN_DT FROM FW.TB_JOB WHERE JOB_ID = @J", ("@J", Job)));
    }

    [Fact]
    public async Task 예약실행시작_잠그고이력을만들고_다음시각을옮긴다_두번째는못가져간다()
    {
        await using var db = await BeginAsync(fixture);
        var next = new DateTime(2030, 1, 1, 0, 10, 0, DateTimeKind.Utc);

        var run = await StartAsync(db, next);

        Assert.NotNull(run);
        Assert.Equal("SCHEDULE|-|RUNNING|-|-|open", await HistAsync(db, run!.HistId));
        Assert.Equal("Y|N|-|locked", await JobStateAsync(db));
        Assert.Equal(next, await db.ScalarAsync<DateTime>("SELECT NEXT_RUN_DT FROM FW.TB_JOB WHERE JOB_ID = @J", ("@J", Job)));
        Assert.Equal(run.HistId, await db.ScalarAsync<long>("SELECT LAST_HIST_ID FROM FW.TB_JOB WHERE JOB_ID = @J", ("@J", Job)));
        Assert.Null(await StartAsync(db, next));
        Assert.Empty(await DueIdsAsync(db));
    }

    [Fact]
    public async Task 수동실행시작_요청자를남기고_예약시각은그대로()
    {
        await using var db = await BeginAsync(fixture, next: "'2030-01-01T00:00:00'");
        await db.ExecAsync("UPDATE FW.TB_JOB SET RUN_REQ_YN = 'Y', RUN_REQ_ID = 'admin1' WHERE JOB_ID = @J", ("@J", Job));

        var run = await StartAsync(db, new DateTime(2031, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(("MANUAL", "admin1"), (run!.RunTypeCd, run.RunUserId));
        Assert.Equal("Y|N|-|locked", await JobStateAsync(db));
        Assert.Equal(new DateTime(2030, 1, 1), await db.ScalarAsync<DateTime>("SELECT NEXT_RUN_DT FROM FW.TB_JOB WHERE JOB_ID = @J", ("@J", Job)));
    }

    [Fact]
    public async Task 멈춘실행은_제한시간뒤넘겨받고_이전이력은INTERRUPTED()
    {
        await using var db = await BeginAsync(fixture);
        var stuck = (await StartAsync(db))!;
        await db.ExecAsync("UPDATE FW.TB_JOB SET LOCK_DT = DATEADD(MINUTE, -31, SYSUTCDATETIME()), NEXT_RUN_DT = DATEADD(MINUTE, -1, SYSUTCDATETIME()) WHERE JOB_ID = @J", ("@J", Job));

        var taken = await StartAsync(db);

        Assert.NotNull(taken);
        Assert.Equal("SCHEDULE|-|FAILED|-|INTERRUPTED|ended", await HistAsync(db, stuck.HistId));
        Assert.Equal(0, (await EndAsync(db, stuck.HistId, "SUCCESS")).AffectedCnt); // 늦게 끝난 이전 실행은 아무것도 바꾸지 않는다
        Assert.Equal("Y|N|-|locked", await JobStateAsync(db));
    }

    [Fact]
    public async Task 끝내기_결과를적고잠금을푼다_두번끝낼수없다()
    {
        await using var db = await BeginAsync(fixture);
        var run = (await StartAsync(db))!;

        Assert.Equal(1, (await EndAsync(db, run.HistId, "SUCCESS", "메모", 5)).AffectedCnt);

        Assert.Equal("SCHEDULE|-|SUCCESS|5|메모|ended", await HistAsync(db, run.HistId));
        Assert.Equal("N|N|SUCCESS|unlocked", await JobStateAsync(db));
        Assert.Equal(0, (await EndAsync(db, run.HistId, "FAILED")).AffectedCnt);
    }

    [Fact]
    public async Task 실패하면_관리자에게메일_연속실패는한번만_성공뒤다시실패하면또()
    {
        await using var db = await BeginAsync(fixture);
        await db.SeedUserAsync("it.jobadm", "HQ", "EMP", null, "PORTAL_ADMIN");
        await db.SeedUserAsync("it.jobemp", "HQ", "EMP", null, "EMP_USER");
        await db.ExecAsync("UPDATE FW.TB_USER SET EMAIL_ENC = 0x0A WHERE USER_ID IN ('it.jobadm', 'it.jobemp')");

        async Task<int> RunAsync(string result)
        {
            await db.ExecAsync("UPDATE FW.TB_JOB SET NEXT_RUN_DT = DATEADD(MINUTE, -1, SYSUTCDATETIME()) WHERE JOB_ID = @J", ("@J", Job));
            var run = (await StartAsync(db))!;
            await EndAsync(db, run.HistId, result, "SqlException: timeout");
            return await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_MSG_QUEUE WHERE TEMPLATE_ID = 'TPL_JOB_FAILED' AND TO_USER_ID IN ('it.jobadm', 'it.jobemp')");
        }

        Assert.Equal(1, await RunAsync("FAILED"));
        Assert.Equal(1, await RunAsync("FAILED"));
        Assert.Equal(1, await RunAsync("SUCCESS"));
        Assert.Equal(2, await RunAsync("FAILED"));

        var json = await db.ScalarAsync<string>("SELECT TOP 1 PARAMS_JSON FROM FW.TB_MSG_QUEUE WHERE TEMPLATE_ID = 'TPL_JOB_FAILED' AND TO_USER_ID = 'it.jobadm'");
        Assert.Equal("""{"JOB_ID":"JOB_IT_TEST","JOB_NM":"테스트","RESULT_MSG":"SqlException: timeout"}""", json);
    }

    [Fact]
    public async Task 세션정리_만료세션을끝내고_오래된세션행을지운다()
    {
        await using var db = await BeginAsync(fixture);
        await db.SeedUserAsync("it.sess", "HQ", "EMP");
        const string Insert = "INSERT INTO FW.TB_USER_SESSION (SESSION_ID, USER_ID, LOGIN_DT, LAST_ACCESS_DT, EXPIRE_DT, AUTH_METHOD_CD, END_DT) VALUES (@S, 'it.sess', SYSUTCDATETIME(), SYSUTCDATETIME(), DATEADD(MINUTE, @E, SYSUTCDATETIME()), 'FORM', DATEADD(DAY, CAST(@D AS INT), SYSUTCDATETIME()))";
        await db.ExecAsync(Insert, ("@S", new string('a', 64)), ("@E", -1), ("@D", null));  // 만료됨
        await db.ExecAsync(Insert, ("@S", new string('b', 64)), ("@E", 30), ("@D", null));  // 사용 중
        await db.ExecAsync(Insert, ("@S", new string('c', 64)), ("@E", -1), ("@D", -31));   // 끝난 지 31일
        await db.ExecAsync(Insert, ("@S", new string('d', 64)), ("@E", -1), ("@D", -29));   // 끝난 지 29일

        await db.Sp.QuerySingleAsync<JobScheduler.SpJobRow>("FW.USP_JOB_SESSION_CLEANUP_EXEC", null, Ct);

        var rows = await db.ColumnAsync<string>(
            "SELECT CONCAT(LEFT(SESSION_ID, 1), '|', IIF(END_DT IS NULL, 'active', ISNULL(END_REASON_CD, 'ended'))) FROM FW.TB_USER_SESSION WHERE USER_ID = 'it.sess' ORDER BY SESSION_ID");
        Assert.Equal(["a|TIMEOUT", "b|active", "d|ended"], rows);
    }

    [Fact]
    public async Task 데이터정리_가입신청이메일_발송끝난메일_오래된이력()
    {
        await using var db = await BeginAsync(fixture);
        const string Join = "INSERT INTO FW.TB_PARTNER_JOIN_REQ (COMPANY_CD, USER_ID, USER_NM, EMAIL_ENC, LANG_CD, REQ_STAT_CD, PROC_DT) VALUES ('HQ', @U, N'신청', 0x01, 'ko', @S, DATEADD(DAY, @D, SYSUTCDATETIME()))";
        await db.ExecAsync(Join, ("@U", "it.join.old"), ("@S", "REJECTED"), ("@D", -31));
        await db.ExecAsync(Join, ("@U", "it.join.new"), ("@S", "APPROVED"), ("@D", -29));
        await db.ExecAsync(Join, ("@U", "it.join.req"), ("@S", "REQUESTED"), ("@D", -100));
        const string Msg = "INSERT INTO FW.TB_MSG_QUEUE (TEMPLATE_ID, LANG_CD, STAT_CD, UPD_DT) VALUES ('TPL_PARTNER_APPROVED', 'ko', @S, DATEADD(DAY, @D, SYSUTCDATETIME()))";
        await db.ExecAsync("DELETE FROM FW.TB_MSG_QUEUE");
        await db.ExecAsync(Msg, ("@S", "SENT"), ("@D", -91));
        await db.ExecAsync(Msg, ("@S", "FAILED"), ("@D", -91));
        await db.ExecAsync(Msg, ("@S", "READY"), ("@D", -91));
        await db.ExecAsync(Msg, ("@S", "SENT"), ("@D", -89));
        const string Hist = "INSERT INTO FW.TB_JOB_HIST (JOB_ID, RUN_TYPE_CD, START_DT, RESULT_CD) VALUES (@J, 'SCHEDULE', DATEADD(DAY, @D, SYSUTCDATETIME()), 'SUCCESS')";
        await db.ExecAsync(Hist, ("@J", Job), ("@D", -366));
        await db.ExecAsync(Hist, ("@J", Job), ("@D", -364));

        var row = await db.Sp.QuerySingleAsync<JobScheduler.SpJobRow>("FW.USP_JOB_DATA_CLEANUP_EXEC", null, Ct);

        Assert.True(row!.ProcessedCnt >= 4);
        Assert.Equal(
            ["it.join.new|email", "it.join.old|-", "it.join.req|email"],
            await db.ColumnAsync<string>("SELECT CONCAT(USER_ID, '|', IIF(EMAIL_ENC IS NULL, '-', 'email')) FROM FW.TB_PARTNER_JOIN_REQ WHERE USER_ID LIKE 'it.join.%' ORDER BY USER_ID"));
        Assert.Equal(["READY", "SENT"], await db.ColumnAsync<string>("SELECT STAT_CD FROM FW.TB_MSG_QUEUE ORDER BY STAT_CD"));
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_JOB_HIST WHERE JOB_ID = @J", ("@J", Job)));
    }

    [Fact]
    public async Task 첨부정리_미확정과오래된삭제파일만_확정된첨부는건드리지않는다()
    {
        await using var db = await BeginAsync(fixture);
        async Task<long> FileAsync(string confirm, int groupAgeDays, string delYn, int updAgeDays, string path)
        {
            await db.ExecAsync(
                "INSERT INTO FW.TB_FILE_GROUP (PROGRAM_ID, COMPANY_CD, CONFIRM_YN, REG_DT) VALUES ('SYS_JOB_MGT', 'HQ', @C, DATEADD(DAY, @A, SYSUTCDATETIME()))",
                ("@C", confirm), ("@A", -groupAgeDays));
            var group = await db.ScalarAsync<long>("SELECT MAX(FILE_GROUP_ID) FROM FW.TB_FILE_GROUP");
            await db.ExecAsync(
                "INSERT INTO FW.TB_FILE (FILE_GROUP_ID, ORG_FILE_NM, FILE_EXT, FILE_SIZE, STORED_PATH, CONTENT_HASH, DEL_YN, UPD_DT) VALUES (@G, N'a.pdf', 'pdf', 1, @P, REPLICATE('0', 64), @D, DATEADD(DAY, @U, SYSUTCDATETIME()))",
                ("@G", group), ("@P", path), ("@D", delYn), ("@U", -updAgeDays));
            return group;
        }

        var tempOld = await FileAsync("N", 2, "N", 0, "it/temp-old");
        await FileAsync("N", 0, "N", 0, "it/temp-new");
        await FileAsync("Y", 100, "N", 100, "it/kept");
        await FileAsync("Y", 100, "Y", 31, "it/deleted-old");
        await FileAsync("Y", 100, "Y", 29, "it/deleted-new");
        var storage = new RecordingStorage();

        var result = await new FileCleanupJob(db.Sp, storage, NullLogger<FileCleanupJob>.Instance).RunAsync(new JobRunContext("JOB_TEMP_CLEANUP", "SCHEDULE", null), Ct);

        // 테스트 DB에 남아 있는 다른 정리 대상이 있을 수 있어 이 테스트의 파일만 본다
        Assert.Equal(["it/deleted-old", "it/temp-old"], storage.Deleted.Where(p => p.StartsWith("it/", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Equal(storage.Deleted.Count, result.ProcessedCount);
        Assert.Equal(["it/deleted-new", "it/kept", "it/temp-new"], await db.ColumnAsync<string>("SELECT STORED_PATH FROM FW.TB_FILE WHERE STORED_PATH LIKE 'it/%' ORDER BY STORED_PATH"));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_FILE_GROUP WHERE FILE_GROUP_ID = @G", ("@G", tempOld))); // 빈 미확정 묶음도 지운다
    }

    [Fact]
    public async Task 첨부정리_목록을받은뒤확정되면_지우지않는다()
    {
        await using var db = await BeginAsync(fixture);
        await db.ExecAsync("INSERT INTO FW.TB_FILE_GROUP (PROGRAM_ID, COMPANY_CD, CONFIRM_YN, REG_DT) VALUES ('SYS_JOB_MGT', 'HQ', 'N', DATEADD(DAY, -2, SYSUTCDATETIME()))");
        var group = await db.ScalarAsync<long>("SELECT MAX(FILE_GROUP_ID) FROM FW.TB_FILE_GROUP");
        await db.ExecAsync("INSERT INTO FW.TB_FILE (FILE_GROUP_ID, ORG_FILE_NM, FILE_EXT, FILE_SIZE, STORED_PATH, CONTENT_HASH) VALUES (@G, N'a.pdf', 'pdf', 1, 'it/race', REPLICATE('0', 64))", ("@G", group));
        var fileId = await db.ScalarAsync<long>("SELECT FILE_ID FROM FW.TB_FILE WHERE STORED_PATH = 'it/race'");
        await db.ExecAsync("UPDATE FW.TB_FILE_GROUP SET CONFIRM_YN = 'Y' WHERE FILE_GROUP_ID = @G", ("@G", group));

        Assert.Equal(0, (await db.Sp.ExecuteAsync(FileCleanupJob.DeleteSp, new { P_FILE_ID = fileId }, Ct)).AffectedCnt);
    }

    [Fact]
    public async Task 실행기_DB와함께_SP작업을돌리고이력을남긴다()
    {
        await using var db = await BeginAsync(fixture, type: "SP", target: "FW.USP_JOB_DATA_CLEANUP_EXEC");
        var services = new ServiceCollection().AddSingleton(db.Sp).BuildServiceProvider();
        var scheduler = new JobScheduler(services.GetRequiredService<IServiceScopeFactory>(), Options.Create(new SchedulerOptions()), TimeProvider.System, NullLogger<JobScheduler>.Instance);

        await Task.WhenAll(await scheduler.PollAsync(Ct));

        var histId = await db.ScalarAsync<long>("SELECT LAST_HIST_ID FROM FW.TB_JOB WHERE JOB_ID = @J", ("@J", Job));
        Assert.StartsWith("SCHEDULE|-|SUCCESS|", await HistAsync(db, histId), StringComparison.Ordinal);
        Assert.Equal("N|N|SUCCESS|unlocked", await JobStateAsync(db));
        Assert.True(await db.ScalarAsync<DateTime>("SELECT NEXT_RUN_DT FROM FW.TB_JOB WHERE JOB_ID = @J", ("@J", Job)) > DateTime.UtcNow);
    }

    private sealed class RecordingStorage : IFileStorage
    {
        public List<string> Deleted { get; } = [];

        public Task WriteAsync(string storedPath, Stream content, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Stream OpenRead(string storedPath) => Stream.Null;

        public void Delete(string storedPath) => Deleted.Add(storedPath);
    }
}
