using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portal.Infrastructure.Scheduling;

namespace Portal.Worker;

/// <summary>
/// 스케줄러 서비스: <c>Portal:Scheduler:PollSeconds</c>마다 실행할 작업(<c>TB_JOB</c>)을 확인해 시작한다.
/// 한 번의 확인 실패(DB 연결 등)로 서비스가 멈추지 않게 오류는 기록하고 다음 간격에 다시 한다.
/// 서비스를 멈추면 돌고 있는 작업에 취소를 알리고 끝날 때까지 기다린다(결과는 STOPPED로 남는다).
/// </summary>
internal sealed partial class SchedulerService(JobScheduler scheduler, IOptions<SchedulerOptions> options, ILogger<SchedulerService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(logger, scheduler.TimeZone.Id);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.PollSeconds)));
        try
        {
            do
            {
                await PollOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 서비스 중지
        }

        await scheduler.WhenIdleAsync();
    }

    /// <summary>한 번 확인한다. 오류는 기록만 한다.</summary>
    internal async Task PollOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await scheduler.PollAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPollFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "스케줄러 시작 (시간대 {TimeZone})")]
    private static partial void LogStarted(ILogger logger, string timeZone);

    [LoggerMessage(Level = LogLevel.Error, Message = "배치 작업 확인 오류 (다음 간격에 다시 합니다)")]
    private static partial void LogPollFailed(ILogger logger, Exception ex);
}
