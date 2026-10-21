namespace Portal.Infrastructure.Caching;

/// <summary>
/// 캐시 버전(<c>FW.TB_CACHE_VERSION</c>)이 바뀌면 메모리 캐시를 다시 읽는 구성요소.
/// 관리 화면의 저장 SP가 버전을 올리면 <see cref="CacheVersionMonitor"/>가 몇 초 안에 알려 준다.
/// </summary>
public interface ICacheVersionListener
{
    /// <summary>담당하는 캐시 키 (<see cref="Core.Keys.CacheKeys"/>).</summary>
    string CacheKey { get; }

    /// <summary>시작할 때 한 번, 그리고 버전이 바뀔 때마다 호출된다.</summary>
    Task ReloadAsync(CancellationToken cancellationToken);
}
