using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Portal.Data;

namespace Portal.Framework.Web.Logging;

/// <summary>
/// 접근 기록 적재 (10년 보관, 해시 체인). 대기열에서 최대 <see cref="BatchSize"/>건씩 모아 TVP 한 번으로 넣는다.
/// 적재에 실패하면 같은 배치를 잠시 후 다시 시도하고(DB 일시 장애), 종료할 때는 남은 기록을 모두 넣고 끝낸다.
/// </summary>
public sealed partial class AccessLogWriter(
    AccessLogBuffer queue,
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<AccessLogWriter> logger) : BackgroundService
{
    internal const string WriteSp = "LOG.USP_ACCESS_LOG_WRITE";
    internal const string TvpType = "LOG.TVP_ACCESS_LOG";
    internal const int BatchSize = 200;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private const int MaxAttempts = 3;

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Complete();
        await base.StopAsync(cancellationToken);
    }

    /// <summary>대기 중인 기록을 한 배치 적재한다. 넣은 건수를 돌려준다.</summary>
    internal async Task<int> FlushOnceAsync(CancellationToken cancellationToken)
    {
        var batch = new List<AccessLogEntry>(BatchSize);
        while (batch.Count < BatchSize && queue.Reader.TryRead(out var entry))
        {
            entry.SeqNo = batch.Count + 1;
            batch.Add(entry);
        }

        if (batch.Count == 0)
        {
            return 0;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var sp = scope.ServiceProvider.GetRequiredService<ISpExecutor>();
                await sp.ExecuteAsync(WriteSp, new { P_ROWS = Tvp.From(TvpType, batch) }, cancellationToken);
                return batch.Count;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < MaxAttempts)
            {
                LogRetry(logger, batch.Count, attempt, ex);
                await Task.Delay(RetryDelay, time, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 계속 실패하면 버리고 다음 배치로 간다 (요청 처리를 막지 않는다). 운영 모니터링이 이 오류를 잡는다.
                LogLost(logger, batch.Count, ex);
                return 0;
            }
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await queue.Reader.WaitToReadAsync(stoppingToken))
            {
                await FlushOnceAsync(stoppingToken);
                if (queue.TakeDroppedCount() is > 0 and var dropped)
                {
                    LogDropped(logger, dropped);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 종료: 남은 기록을 마저 넣는다
            while (await FlushOnceAsync(CancellationToken.None) > 0)
            {
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "접근 기록 {Count}건 적재 실패 ({Attempt}회), 다시 시도합니다")]
    private static partial void LogRetry(ILogger logger, int count, int attempt, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "접근 기록 {Count}건을 적재하지 못해 버렸습니다")]
    private static partial void LogLost(ILogger logger, int count, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "접근 기록 대기열이 가득 차 {Count}건을 버렸습니다")]
    private static partial void LogDropped(ILogger logger, long count);
}
