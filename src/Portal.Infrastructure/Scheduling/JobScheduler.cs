using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portal.Core.Errors;
using Portal.Core.Scheduling;
using Portal.Data;

namespace Portal.Infrastructure.Scheduling;

/// <summary>스케줄러 설정 (<c>Portal:Scheduler</c>, Portal.Worker의 appsettings).</summary>
public sealed class SchedulerOptions
{
    /// <summary>설정 섹션 이름.</summary>
    public const string SectionName = "Portal:Scheduler";

    /// <summary>실행할 작업을 확인하는 간격(초). 수동 실행 요청도 이 간격 안에 시작된다.</summary>
    public int PollSeconds { get; set; } = 15;

    /// <summary>Cron 식을 해석하는 시간대 (IANA 또는 Windows ID). 기본 한국 시간.</summary>
    public string TimeZone { get; set; } = "Asia/Seoul";
}

/// <summary>
/// 배치 작업 실행기 (설계서 13장 스케줄러). Portal.Worker가 <see cref="SchedulerOptions.PollSeconds"/>마다 <see cref="PollAsync"/>를 부른다.
/// 실행할 작업을 DB에서 잠가 가져가므로(<c>USP_JOB_START_EXEC</c>) Worker가 여러 대여도 같은 작업이 동시에 돌지 않는다.
/// 작업마다 따로 실행하므로 오래 걸리는 야간 작업이 1분마다 도는 메일 발송을 막지 않는다.
/// 예약 실행을 놓쳤으면(서비스 중지 등) 밀린 횟수만큼 돌리지 않고 한 번만 실행한 뒤 다음 주기로 넘어간다.
/// </summary>
public sealed partial class JobScheduler(IServiceScopeFactory scopes, IOptions<SchedulerOptions> options, TimeProvider time, ILogger<JobScheduler> logger)
{
    internal const string DueSp = "FW.USP_JOB_DUE_GET_LIST";
    internal const string NextRunSp = "FW.USP_JOB_NEXT_RUN_SAVE";
    internal const string StartSp = "FW.USP_JOB_START_EXEC";
    internal const string EndSp = "FW.USP_JOB_END_EXEC";

    private readonly ConcurrentDictionary<string, Task> _running = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _reportedBadCron = new(StringComparer.Ordinal);
    private readonly TimeZoneInfo _timeZone = ResolveTimeZone(options.Value.TimeZone);

    /// <summary>Cron을 해석하는 시간대.</summary>
    public TimeZoneInfo TimeZone => _timeZone;

    /// <summary>
    /// 실행할 차례인 작업을 시작한다. 시작한 작업은 끝날 때까지 기다리지 않는다(돌려준 Task로 기다릴 수 있다).
    /// 이 Worker에서 이미 돌고 있는 작업은 건너뛴다.
    /// </summary>
    public async Task<IReadOnlyList<Task>> PollAsync(CancellationToken stoppingToken)
    {
        var started = new List<Task>();
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider.GetRequiredService<ISpExecutor>();
        var due = await sp.QueryAsync<DueJob>(DueSp, null, stoppingToken);

        foreach (var job in due)
        {
            if (_running.ContainsKey(job.JobId))
            {
                continue;
            }

            var next = NextRun(job);
            if (job.RunReqYn != "Y" && job.NextRunDt is null)
            {
                // 새로 등록했거나 주기를 바꾼 작업: 바로 돌리지 않고 다음 예약 시각만 채운다
                if (next is not null)
                {
                    await sp.ExecuteAsync(NextRunSp, new { P_JOB_ID = job.JobId, P_NEXT_RUN_DT = next }, stoppingToken);
                }

                continue;
            }

            var run = await sp.QuerySingleAsync<StartedRun>(StartSp, new { P_JOB_ID = job.JobId, P_NEXT_RUN_DT = next, P_HOST_NM = HostName() }, stoppingToken);
            if (run is null)
            {
                continue;
            }

            var task = RunAsync(job, run, stoppingToken);
            _running[job.JobId] = task;
            _ = task.ContinueWith(_ => _running.TryRemove(job.JobId, out Task? _), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            started.Add(task);
        }

        return started;
    }

    /// <summary>이 Worker에서 돌고 있는 작업이 모두 끝날 때까지 기다린다(서비스 중지 시).</summary>
    public Task WhenIdleAsync() => Task.WhenAll(_running.Values);

    private DateTime? NextRun(DueJob job)
    {
        if (string.IsNullOrWhiteSpace(job.CronExpr))
        {
            return null;
        }

        if (!CronSchedule.TryParse(job.CronExpr, out var cron))
        {
            if (_reportedBadCron.TryAdd(job.JobId + "|" + job.CronExpr, 0))
            {
                LogBadCron(logger, job.JobId, job.CronExpr);
            }

            return null;
        }

        return cron!.NextUtc(time.GetUtcNow().UtcDateTime, _timeZone);
    }

    private async Task RunAsync(DueJob job, StartedRun run, CancellationToken stoppingToken)
    {
        await Task.Yield();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        limit.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, job.TimeoutMin)));

        string resultCd;
        int? processed = null;
        string? message = null;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await ExecuteAsync(scope.ServiceProvider, job, new JobRunContext(job.JobId, run.RunTypeCd, run.RunUserId), limit.Token);
            resultCd = result.Skipped ? "SKIPPED" : "SUCCESS";
            (processed, message) = (result.ProcessedCount, result.Message);
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested)
        {
            resultCd = "FAILED";
            message = stoppingToken.IsCancellationRequested ? "STOPPED" : $"TIMEOUT ({job.TimeoutMin}분)";
            LogJobFailed(logger, job.JobId, message, null);
        }
        catch (Exception ex)
        {
            resultCd = "FAILED";
            message = Describe(ex);
            LogJobFailed(logger, job.JobId, message, ex);
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ISpExecutor>().ExecuteAsync(EndSp, new
            {
                P_HIST_ID = run.HistId,
                P_RESULT_CD = resultCd,
                P_PROCESSED_CNT = processed,
                P_RESULT_MSG = message is { Length: > 1000 } ? message[..1000] : message,
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // 결과를 못 적으면 잠금이 남지만, TIMEOUT_MIN이 지나면 다음 실행이 넘겨받는다
            LogEndFailed(logger, job.JobId, ex);
        }
    }

    private static async Task<JobRunResult> ExecuteAsync(IServiceProvider services, DueJob job, JobRunContext context, CancellationToken cancellationToken)
    {
        switch (job.JobTypeCd)
        {
            case "CLASS":
                var target = services.GetServices<IScheduledJob>().FirstOrDefault(j => string.Equals(j.Name, job.JobTarget, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException($"JOB_TARGET_NOT_FOUND: '{job.JobTarget}' 이름의 IScheduledJob이 등록되어 있지 않습니다.");
                return await target.RunAsync(context, cancellationToken);
            case "SP":
                if (!JobSpPattern().IsMatch(job.JobTarget))
                {
                    throw new InvalidOperationException($"JOB_TARGET_INVALID: SP 작업은 스키마.USP_JOB_..._EXEC 이름만 실행합니다: '{job.JobTarget}'");
                }

                var row = await services.GetRequiredService<ISpExecutor>().QuerySingleAsync<SpJobRow>(job.JobTarget, null, cancellationToken);
                return new JobRunResult(row?.ProcessedCnt);
            default:
                throw new InvalidOperationException($"JOB_TYPE_INVALID: '{job.JobTypeCd}'");
        }
    }

    /// <summary>실패 사유. 업무 오류는 메시지 키, 그 밖에는 예외 형식과 메시지(이력은 관리자만 본다).</summary>
    private static string Describe(Exception ex) => ex is BizException biz ? biz.MessageKey : $"{ex.GetType().Name}: {ex.Message}";

    private static string HostName()
    {
        var name = Environment.MachineName;
        return name.Length > 64 ? name[..64] : name;
    }

    internal static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
        {
            return zone;
        }

        // ICU가 없는 Windows 등에서 IANA ID를 못 찾으면 Windows ID로 한 번 더 찾는다
        if (!string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId) && TimeZoneInfo.TryFindSystemTimeZoneById(windowsId, out zone))
        {
            return zone;
        }

        throw new InvalidOperationException($"{SchedulerOptions.SectionName}:TimeZone '{id}' 시간대를 찾을 수 없습니다.");
    }

    [GeneratedRegex(@"^[A-Z][A-Z0-9]*\.USP_JOB_[A-Z0-9_]+_EXEC$")]
    private static partial Regex JobSpPattern();

    [LoggerMessage(Level = LogLevel.Warning, Message = "배치 작업 {JobId}의 주기 '{CronExpr}'를 해석할 수 없어 예약 실행하지 않습니다 (스케줄 관리에서 고치세요)")]
    private static partial void LogBadCron(ILogger logger, string jobId, string? cronExpr);

    [LoggerMessage(Level = LogLevel.Error, Message = "배치 작업 {JobId} 실패: {Reason}")]
    private static partial void LogJobFailed(ILogger logger, string jobId, string reason, Exception? ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "배치 작업 {JobId}의 결과를 기록하지 못했습니다")]
    private static partial void LogEndFailed(ILogger logger, string jobId, Exception ex);

    /// <summary>실행할 차례인 작업 (USP_JOB_DUE_GET_LIST).</summary>
    internal sealed class DueJob
    {
        public string JobId { get; set; } = "";

        public string JobTypeCd { get; set; } = "";

        public string JobTarget { get; set; } = "";

        public string? CronExpr { get; set; }

        public int TimeoutMin { get; set; }

        public DateTime? NextRunDt { get; set; }

        public string RunReqYn { get; set; } = "N";
    }

    /// <summary>시작한 실행 (USP_JOB_START_EXEC).</summary>
    internal sealed class StartedRun
    {
        public long HistId { get; set; }

        public string RunTypeCd { get; set; } = "SCHEDULE";

        public string? RunUserId { get; set; }
    }

    /// <summary>SP 작업의 결과 행.</summary>
    internal sealed class SpJobRow
    {
        public int? ProcessedCnt { get; set; }
    }
}
