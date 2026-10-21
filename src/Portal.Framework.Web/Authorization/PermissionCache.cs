using Microsoft.Extensions.Caching.Memory;
using Portal.Core.Keys;
using Portal.Infrastructure.Caching;
using Portal.Security.Authorization;

namespace Portal.Framework.Web.Authorization;

/// <summary>
/// 사용자별 권한 캐시 (설계서 6장 "로그인 시 계산해 사용자별로 캐시"). 권한 데이터가 바뀌어 캐시 버전 <c>AUTH</c>가 오르면
/// 세대 번호를 올려 모든 사용자의 권한을 다음 요청에서 다시 계산한다.
/// </summary>
public sealed class PermissionCache(IMemoryCache cache) : ICacheVersionListener
{
    private static readonly MemoryCacheEntryOptions EntryOptions = new() { SlidingExpiration = TimeSpan.FromMinutes(30), Size = 1 };

    private long _generation;

    /// <inheritdoc />
    public string CacheKey => CacheKeys.Auth;

    /// <summary>캐시된 권한을 주거나 <paramref name="load"/>로 계산해 넣는다.</summary>
    public async Task<PermissionSet> GetOrLoadAsync(string userId, Func<Task<PermissionSet>> load)
    {
        var key = (nameof(PermissionCache), userId, Interlocked.Read(ref _generation));
        if (cache.TryGetValue(key, out PermissionSet? cached) && cached is not null)
        {
            return cached;
        }

        var loaded = await load();
        cache.Set(key, loaded, EntryOptions);
        return loaded;
    }

    /// <inheritdoc />
    public Task ReloadAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _generation);
        return Task.CompletedTask;
    }
}
