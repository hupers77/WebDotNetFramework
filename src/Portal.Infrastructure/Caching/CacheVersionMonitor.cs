using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portal.Data;

namespace Portal.Infrastructure.Caching;

/// <summary>
/// 캐시 버전을 주기적으로 읽어 바뀐 키의 <see cref="ICacheVersionListener"/>를 호출한다(설계서 4장).
/// 시작할 때 모든 캐시를 한 번 읽고 나서야 요청을 받는다(설정 없이 뜨지 않도록). 웹서버가 늘어도 같은 방식으로 맞춰진다.
/// </summary>
public sealed partial class CacheVersionMonitor(
    IServiceScopeFactory scopeFactory,
    IEnumerable<ICacheVersionListener> listeners,
    IOptions<CacheOptions> options,
    TimeProvider time,
    ILogger<CacheVersionMonitor> logger) : BackgroundService
{
    internal const string VersionSp = "FW.USP_CACHE_VERSION_GET_LIST";

    private readonly ICacheVersionListener[] listeners = [.. listeners];
    private Dictionary<string, long> versions = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        versions = await ReadVersionsAsync(cancellationToken);
        foreach (var listener in listeners)
        {
            await listener.ReloadAsync(cancellationToken);
        }

        await base.StartAsync(cancellationToken);
    }

    /// <summary>한 번 확인한다. 바뀐 키의 리스너만 다시 읽고, 실패한 리스너는 다음 주기에 다시 시도한다.</summary>
    internal async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        var latest = await ReadVersionsAsync(cancellationToken);
        foreach (var listener in listeners)
        {
            latest.TryGetValue(listener.CacheKey, out var now);
            versions.TryGetValue(listener.CacheKey, out var before);
            if (now == before)
            {
                continue;
            }

            try
            {
                await listener.ReloadAsync(cancellationToken);
                versions[listener.CacheKey] = now;
                LogReloaded(logger, listener.CacheKey, now);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogReloadFailed(logger, listener.CacheKey, ex);
            }
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.PollSeconds)), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CheckOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // DB 일시 장애: 기존 캐시로 계속 서비스하고 다음 주기에 다시 확인한다
                LogPollFailed(logger, ex);
            }
        }
    }

    private async Task<Dictionary<string, long>> ReadVersionsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider.GetRequiredService<ISpExecutor>();
        var rows = await sp.QueryAsync<VersionRow>(VersionSp, null, cancellationToken);
        return rows.ToDictionary(r => r.CacheKey, r => r.VersionNo, StringComparer.Ordinal);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "캐시 {CacheKey}를 다시 읽었습니다 (버전 {Version})")]
    private static partial void LogReloaded(ILogger logger, string cacheKey, long version);

    [LoggerMessage(Level = LogLevel.Error, Message = "캐시 {CacheKey}를 다시 읽지 못했습니다. 다음 주기에 다시 시도합니다")]
    private static partial void LogReloadFailed(ILogger logger, string cacheKey, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "캐시 버전을 확인하지 못했습니다")]
    private static partial void LogPollFailed(ILogger logger, Exception ex);

    internal sealed class VersionRow
    {
        public string CacheKey { get; set; } = "";

        public long VersionNo { get; set; }
    }
}
