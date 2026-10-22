using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portal.Core.Errors;
using Portal.Core.Scheduling;
using Portal.Data;
using Portal.Infrastructure.Scheduling;
using Portal.Testing;

namespace Portal.UnitTests.Infrastructure;

public sealed class JobSchedulerTests
{
    // 한국 시간 2026-10-22 10:07:30
    private static readonly DateTime Now = new(2026, 10, 22, 1, 7, 30, DateTimeKind.Utc);

    private readonly FakeSpExecutor _sp = new();
    private readonly TestJob _job = new("Test");

    private JobScheduler NewScheduler(params IScheduledJob[] jobs)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISpExecutor>(_sp);
        foreach (var job in jobs.Length == 0 ? [_job] : jobs)
        {
            services.AddSingleton(job);
        }

        return new JobScheduler(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), Options.Create(new SchedulerOptions()), new FixedTime(Now), NullLogger<JobScheduler>.Instance);
    }

    private static JobScheduler.DueJob Due(string id = "JOB_T", string type = "CLASS", string target = "Test", string? cron = "*/10 * * * *", DateTime? next = null, string req = "N", int timeout = 60) => new()
    {
        JobId = id,
        JobTypeCd = type,
        JobTarget = target,
        CronExpr = cron,
        TimeoutMin = timeout,
        NextRunDt = next ?? Now.AddMinutes(-1),
        RunReqYn = req,
    };

    private void Started(long histId = 7, string runType = "SCHEDULE", string? user = null) =>
        _sp.Returns(JobScheduler.StartSp, new JobScheduler.StartedRun { HistId = histId, RunTypeCd = runType, RunUserId = user });

    private static async Task PollAndWaitAsync(JobScheduler scheduler, CancellationToken cancellationToken = default) =>
        await Task.WhenAll(await scheduler.PollAsync(cancellationToken));

    private IReadOnlyDictionary<string, object?> End() => _sp.LastCall(JobScheduler.EndSp).Parameters;

    [Fact]
    public async Task 예약시각이지난작업_다음시각을넘겨시작하고_결과를기록한다()
    {
        _sp.Returns(JobScheduler.DueSp, new List<JobScheduler.DueJob> { Due() });
        Started();
        _job.Result = JobRunResult.Done(3, "메모");

        await PollAndWaitAsync(NewScheduler());

        var start = _sp.LastCall(JobScheduler.StartSp).Parameters;
        Assert.Equal("JOB_T", start["P_JOB_ID"]);
        Assert.Equal(new DateTime(2026, 10, 22, 1, 10, 0, DateTimeKind.Utc), start["P_NEXT_RUN_DT"]); // 한국 시간 10:10
        Assert.Equal(new JobRunContext("JOB_T", "SCHEDULE", null), _job.LastContext);
        Assert.Equal((7L, "SUCCESS", (object?)3, (object?)"메모"), ((long)End()["P_HIST_ID"]!, (string)End()["P_RESULT_CD"]!, End()["P_PROCESSED_CNT"], End()["P_RESULT_MSG"]));
    }

    [Fact]
    public async Task 수동실행_요청자정보를넘기고_건너뜀은SKIPPED()
    {
        _sp.Returns(JobScheduler.DueSp, new List<JobScheduler.DueJob> { Due(cron: null, next: Now.AddDays(1), req: "Y") });
        Started(runType: "MANUAL", user: "admin1");
        _job.Result = JobRunResult.Skip("꺼짐");

        await PollAndWaitAsync(NewScheduler());

        Assert.Null(_sp.LastCall(JobScheduler.StartSp).Parameters["P_NEXT_RUN_DT"]);
        Assert.Equal(new JobRunContext("JOB_T", "MANUAL", "admin1"), _job.LastContext);
        Assert.Equal("SKIPPED", End()["P_RESULT_CD"]);
    }

    [Fact]
    public async Task 다음시각이없는새작업_실행하지않고_다음시각만채운다()
    {
        var job = Due();
        job.NextRunDt = null;
        _sp.Returns(JobScheduler.DueSp, new List<JobScheduler.DueJob> { job });

        await PollAndWaitAsync(NewScheduler());

        Assert.Equal(new DateTime(2026, 10, 22, 1, 10, 0, DateTimeKind.Utc), _sp.LastCall(JobScheduler.NextRunSp).Parameters["P_NEXT_RUN_DT"]);
        Assert.DoesNotContain(_sp.Calls, c => c.SpName == JobScheduler.StartSp);
        Assert.Equal(0, _job.RunCount);
    }

    [Fact]
    public async Task 다른Worker가먼저가져가면_실행하지않는다()
    {
        _sp.Returns(JobScheduler.DueSp, new List<JobScheduler.DueJob> { Due() });

        await PollAndWaitAsync(NewScheduler());

        Assert.Equal(0, _job.RunCount);
        Assert.DoesNotContain(_sp.Calls, c => c.SpName == JobScheduler.EndSp);
    }

    [Fact]
    public async Task 이Worker에서돌고있는작업은_다시시작하지않는다()
    {
        _sp.Returns(JobScheduler.DueSp, new List<JobScheduler.DueJob> { Due() });
        Started();
        _job.Gate = new TaskCompletionSource();
        var scheduler = NewScheduler();

        var first = await scheduler.PollAsync(CancellationToken.None);
        var second = await scheduler.PollAsync(CancellationToken.None);
        _job.Gate.SetResult();
        await Task.WhenAll(first);
        await scheduler.WhenIdleAsync();

        await Assert.Single(first);
        Assert.Empty(second);
        Assert.Single(_sp.Calls, c => c.SpName == JobScheduler.StartSp);
    }

    [Fact]
    public async Task 예외는FAILED로기록_업무오류는메시지키()
    {
        _sp.Returns(JobScheduler.DueSp, new List<JobScheduler.DueJob> { Due() });
        Started();
        _job.Error = new InvalidOperationException("연결 실패");
        await PollAndWaitAsync(NewScheduler());
        Assert.Equal(("FAILED", "InvalidOperationException: 연결 실패"), ((string)End()["P_RESULT_CD"]!, (string?)End()["P_RESULT_MSG"]));

        _job.Error = new BizException("JOB.SOMETHING");
        await PollAndWaitAsync(NewScheduler());
        Assert.Equal("JOB.SOMETHING", End()["P_RESULT_MSG"]);
    }

    [Fact]
    public async Task 서비스가멈추면_돌던작업을취소하고STOPPED()
    {
        _sp.Returns(JobScheduler.DueSp, new List<JobScheduler.DueJob> { Due(timeout: 1) });
        Started();
        _job.Gate = new TaskCompletionSource(); // 취소될 때까지 끝나지 않는다
        var scheduler = NewScheduler();
        using var stop = new CancellationTokenSource();

        var tasks = await scheduler.PollAsync(stop.Token);
        await stop.CancelAsync();
        await Task.WhenAll(tasks);

        Assert.Equal(("FAILED", "STOPPED"), ((string)End()["P_RESULT_CD"]!, (string?)End()["P_RESULT_MSG"]));
    }

    [Theory]
    [InlineData("SP", "FW.USP_CODE_DEL", "JOB_TARGET_INVALID")]
    [InlineData("SP", "FW.USP_JOB_X_EXEC; DROP TABLE X", "JOB_TARGET_INVALID")]
    [InlineData("CLASS", "Nope", "JOB_TARGET_NOT_FOUND")]
    [InlineData("SHELL", "x", "JOB_TYPE_INVALID")]
    public async Task 실행대상이잘못되면_실행하지않고FAILED(string type, string target, string code)
    {
        _sp.Returns(JobScheduler.DueSp, new List<JobScheduler.DueJob> { Due(type: type, target: target) });
        Started();

        await PollAndWaitAsync(NewScheduler());

        Assert.StartsWith("InvalidOperationException: " + code, (string?)End()["P_RESULT_MSG"]);
        Assert.DoesNotContain(_sp.Calls, c => c.SpName == target);
    }

    [Fact]
    public async Task SP작업_처리건수를기록한다()
    {
        _sp.Returns(JobScheduler.DueSp, new List<JobScheduler.DueJob> { Due(type: "SP", target: "FW.USP_JOB_DATA_CLEANUP_EXEC") });
        _sp.Returns("FW.USP_JOB_DATA_CLEANUP_EXEC", new JobScheduler.SpJobRow { ProcessedCnt = 12 });
        Started();

        await PollAndWaitAsync(NewScheduler());

        Assert.Equal(("SUCCESS", (object?)12), ((string)End()["P_RESULT_CD"]!, End()["P_PROCESSED_CNT"]));
    }

    [Fact]
    public async Task 주기를읽을수없으면_예약하지않고_수동실행은된다()
    {
        var bad = Due(cron: "every minute");
        bad.NextRunDt = null;
        _sp.Returns(JobScheduler.DueSp, new List<JobScheduler.DueJob> { bad });
        await PollAndWaitAsync(NewScheduler());
        Assert.DoesNotContain(_sp.Calls, c => c.SpName is JobScheduler.NextRunSp or JobScheduler.StartSp);

        bad.RunReqYn = "Y";
        Started();
        await PollAndWaitAsync(NewScheduler());
        Assert.Equal("SUCCESS", End()["P_RESULT_CD"]);
    }

    [Fact]
    public async Task 결과기록이실패해도_예외를던지지않는다()
    {
        _sp.Returns(JobScheduler.DueSp, new List<JobScheduler.DueJob> { Due() });
        Started();
        _sp.Throws(JobScheduler.EndSp, new InvalidOperationException("DB 끊김"));

        await PollAndWaitAsync(NewScheduler());

        Assert.Equal(1, _job.RunCount);
    }

    private sealed class TestJob(string name) : IScheduledJob
    {
        public string Name => name;

        public JobRunResult Result { get; set; } = JobRunResult.Done(0);

        public Exception? Error { get; set; }

        public TaskCompletionSource? Gate { get; set; }

        public JobRunContext? LastContext { get; private set; }

        public int RunCount { get; private set; }

        public async Task<JobRunResult> RunAsync(JobRunContext context, CancellationToken cancellationToken)
        {
            RunCount++;
            LastContext = context;
            if (Gate is not null)
            {
                await Gate.Task.WaitAsync(cancellationToken);
            }

            return Error is null ? Result : throw Error;
        }
    }

    private sealed class FixedTime(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
