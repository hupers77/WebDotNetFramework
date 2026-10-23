using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Portal.Data;

namespace Portal.Framework.Web.Api;

/// <summary>API 기록 대기열 (<see cref="Logging.AccessLogBuffer"/>와 같은 방식: 응답을 늦추지 않고, 가득 차면 오래된 기록부터 버린다).</summary>
public sealed class ApiLogBuffer
{
    internal const int Capacity = 5_000;

    private readonly Channel<ApiLogEntry> _channel = Channel.CreateBounded<ApiLogEntry>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    internal ChannelReader<ApiLogEntry> Reader => _channel.Reader;

    /// <summary>기록을 넣는다.</summary>
    public void Enqueue(ApiLogEntry entry) => _channel.Writer.TryWrite(entry);

    internal void Complete() => _channel.Writer.TryComplete();
}

/// <summary>API 기록 적재. 대기열에서 최대 <see cref="BatchSize"/>건씩 JSON 한 번으로 넣고, 실패하면 잠시 후 다시 시도한다.</summary>
public sealed partial class ApiLogWriter(ApiLogBuffer queue, IServiceScopeFactory scopeFactory, TimeProvider time, ILogger<ApiLogWriter> logger) : BackgroundService
{
    internal const string WriteSp = "LOG.USP_API_LOG_WRITE";
    internal const int BatchSize = 100;
    private const int MaxAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Complete();
        await base.StopAsync(cancellationToken);
    }

    /// <summary>대기 중인 기록을 한 배치 적재한다. 넣은 건수를 돌려준다.</summary>
    internal async Task<int> FlushOnceAsync(CancellationToken cancellationToken)
    {
        var batch = new List<ApiLogEntry>(BatchSize);
        while (batch.Count < BatchSize && queue.Reader.TryRead(out var entry))
        {
            batch.Add(entry);
        }

        if (batch.Count == 0)
        {
            return 0;
        }

        var rows = JsonSerializer.Serialize(batch.Select(e => new
        {
            LogDt = e.LogDt.ToString("yyyy-MM-ddTHH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture),
            e.DirectionCd, e.ClientId, e.HttpMethod, e.Url, e.StatusCode, e.ElapsedMs, e.RequestBody, e.ResponseBody, e.TraceId,
        }), Json);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var sp = scope.ServiceProvider.GetRequiredService<ISpExecutor>();
                await sp.ExecuteAsync(WriteSp, new { P_ROWS_JSON = rows }, cancellationToken);
                return batch.Count;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < MaxAttempts)
            {
                LogRetry(logger, batch.Count, attempt, ex);
                await Task.Delay(RetryDelay, time, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
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
            }
        }
        catch (OperationCanceledException)
        {
            while (await FlushOnceAsync(CancellationToken.None) > 0)
            {
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "API 기록 {Count}건 적재 실패 ({Attempt}회), 다시 시도합니다")]
    private static partial void LogRetry(ILogger logger, int count, int attempt, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "API 기록 {Count}건을 적재하지 못해 버렸습니다")]
    private static partial void LogLost(ILogger logger, int count, Exception ex);
}
