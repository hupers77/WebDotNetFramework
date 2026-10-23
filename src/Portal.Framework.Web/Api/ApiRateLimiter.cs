namespace Portal.Framework.Web.Api;

/// <summary>분당 호출 한도 (고정 1분 구간, 서버 메모리). 서버가 1대이므로 메모리만 쓴다.</summary>
public sealed class ApiRateLimiter(TimeProvider time)
{
    private const int PruneThreshold = 10_000;

    private readonly object _gate = new();
    private readonly Dictionary<string, (long Minute, int Count)> _windows = new(StringComparer.Ordinal);

    /// <summary>한 번 호출을 허용하는지. 한도를 넘으면 false와 다음 구간까지 남은 초.</summary>
    public bool TryAcquire(string key, int limit, out int retryAfterSeconds)
    {
        var now = time.GetUtcNow();
        var minute = now.ToUnixTimeSeconds() / 60;
        retryAfterSeconds = (int)(((minute + 1) * 60) - now.ToUnixTimeSeconds());
        lock (_gate)
        {
            if (_windows.Count > PruneThreshold)
            {
                foreach (var stale in _windows.Where(w => w.Value.Minute < minute).Select(w => w.Key).ToList())
                {
                    _windows.Remove(stale);
                }
            }

            var count = _windows.TryGetValue(key, out var w) && w.Minute == minute ? w.Count : 0;
            if (count >= limit)
            {
                return false;
            }

            _windows[key] = (minute, count + 1);
            return true;
        }
    }
}
