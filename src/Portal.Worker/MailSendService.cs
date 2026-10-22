using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portal.Infrastructure.Messaging;

namespace Portal.Worker;

/// <summary>Portal.Worker 설정 (<c>Portal:Worker</c>).</summary>
public sealed class WorkerOptions
{
    /// <summary>설정 섹션 이름.</summary>
    public const string SectionName = "Portal:Worker";

    /// <summary>메일 대기열 확인 간격(초).</summary>
    public int MailIntervalSeconds { get; set; } = 60;
}

/// <summary>
/// 메일 발송 작업(<c>JOB_MAIL_SEND</c>): 정해진 간격마다 대기열을 한 묶음씩 보낸다. 보낼 것이 남아 있으면 바로 다음 묶음을 보낸다.
/// 한 번의 실패(DB 연결 등)로 서비스가 멈추지 않게 오류는 기록하고 다음 간격에 다시 한다.
/// 스케줄러(<c>TB_JOB</c>)가 들어오면 이 작업도 스케줄러에 등록한다.
/// </summary>
internal sealed partial class MailSendService(IServiceScopeFactory scopes, IOptions<WorkerOptions> options, ILogger<MailSendService> logger) : BackgroundService
{
    /// <summary>한 간격에 보내는 최대 묶음 수 (대기열이 많아도 다른 작업을 막지 않게).</summary>
    internal const int MaxBatchesPerTick = 10;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.MailIntervalSeconds)));
        do
        {
            await RunTickAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>한 간격의 일: 보낼 것이 없거나 꺼져 있을 때까지 묶음을 보낸다.</summary>
    internal async Task RunTickAsync(CancellationToken cancellationToken)
    {
        try
        {
            for (var i = 0; i < MaxBatchesPerTick; i++)
            {
                await using var scope = scopes.CreateAsyncScope();
                var summary = await scope.ServiceProvider.GetRequiredService<MailQueueSender>().RunOnceAsync(cancellationToken);
                if (summary.Sent + summary.Failed > 0)
                {
                    LogBatch(logger, summary.Sent, summary.Failed);
                }

                if (!summary.Enabled || summary.Sent + summary.Failed < MailQueueSender.BatchSize)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogTickFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "메일 발송: 성공 {Sent}, 실패 {Failed}")]
    private static partial void LogBatch(ILogger logger, int sent, int failed);

    [LoggerMessage(Level = LogLevel.Error, Message = "메일 발송 작업 오류 (다음 간격에 다시 합니다)")]
    private static partial void LogTickFailed(ILogger logger, Exception ex);
}
