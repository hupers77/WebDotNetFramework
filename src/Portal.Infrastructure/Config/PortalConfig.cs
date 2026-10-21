using System.Collections.Frozen;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Portal.Core.Config;
using Portal.Core.Keys;
using Portal.Data;
using Portal.Infrastructure.Caching;

namespace Portal.Infrastructure.Config;

/// <summary>
/// <see cref="IPortalConfig"/> 구현. <c>FW.TB_CONFIG</c> 전체를 메모리에 두고, 캐시 버전 <c>CONFIG</c>가 바뀌면 통째로 바꾼다.
/// 읽기는 잠금 없이 현재 스냅숏을 본다.
/// </summary>
public sealed class PortalConfig(IServiceScopeFactory scopeFactory) : IPortalConfig, ICacheVersionListener
{
    internal const string ListSp = "FW.USP_CONFIG_GET_LIST";

    private volatile FrozenDictionary<string, Entry>? snapshot;

    /// <inheritdoc />
    public string CacheKey => CacheKeys.Config;

    /// <inheritdoc />
    public string? GetString(string key)
    {
        var entry = Find(key);
        if (entry is null)
        {
            return null;
        }

        if (entry.Encrypted)
        {
            throw new ConfigValueException(key, "암호화된 값은 비밀값 전용 API로 읽어야 합니다");
        }

        return string.IsNullOrEmpty(entry.Value) ? null : entry.Value;
    }

    /// <inheritdoc />
    public int GetInt(string key) =>
        int.TryParse(GetString(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new ConfigValueException(key, "정수 값이 없습니다");

    /// <inheritdoc />
    public bool GetBool(string key) => GetString(key) switch
    {
        "Y" => true,
        "N" => false,
        _ => throw new ConfigValueException(key, "Y/N 값이 없습니다"),
    };

    /// <inheritdoc />
    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider.GetRequiredService<ISpExecutor>();
        var rows = await sp.QueryAsync<ConfigRow>(ListSp, null, cancellationToken);
        snapshot = rows.ToFrozenDictionary(
            r => r.ConfigKey,
            r => new Entry(r.ConfigVal, r.EncYn == "Y"),
            StringComparer.Ordinal);
    }

    private Entry? Find(string key)
    {
        var current = snapshot ?? throw new InvalidOperationException("설정을 아직 읽지 않았습니다 (CacheVersionMonitor가 시작 시 읽는다).");
        return current.GetValueOrDefault(key);
    }

    private sealed record Entry(string? Value, bool Encrypted);

    internal sealed class ConfigRow
    {
        public string ConfigKey { get; set; } = "";

        public string? ConfigVal { get; set; }

        public string ValTypeCd { get; set; } = "";

        public string EncYn { get; set; } = "N";
    }
}
