namespace Portal.Framework.Web.Api.Outbound;

/// <summary>
/// 대상별 회로 차단. 연속 <see cref="Threshold"/>번 실패하면 <see cref="OpenSeconds"/>초 동안 호출하지 않고 바로 실패시켜 장애 대상에 요청이 쌓이는 것을 막는다.
/// 시간이 지나면 한 번 시도해 보고(실패하면 다시 차단), 성공하면 풀린다. 서버 메모리만 쓴다.
/// </summary>
public sealed class OutboundCircuitBreaker(TimeProvider time)
{
    /// <summary>차단까지의 연속 실패 횟수.</summary>
    public const int Threshold = 5;

    /// <summary>차단 시간(초).</summary>
    public const int OpenSeconds = 30;

    private readonly object _gate = new();
    private readonly Dictionary<string, (int Failures, DateTimeOffset OpenUntil)> _state = new(StringComparer.Ordinal);

    /// <summary>지금 차단 중인지.</summary>
    public bool IsOpen(string key)
    {
        lock (_gate)
        {
            return _state.TryGetValue(key, out var s) && s.Failures >= Threshold && time.GetUtcNow() < s.OpenUntil;
        }
    }

    /// <summary>실패를 기록한다.</summary>
    public void RecordFailure(string key)
    {
        lock (_gate)
        {
            var failures = (_state.TryGetValue(key, out var s) ? s.Failures : 0) + 1;
            _state[key] = (failures, failures >= Threshold ? time.GetUtcNow().AddSeconds(OpenSeconds) : default);
        }
    }

    /// <summary>성공을 기록한다 (차단 해제).</summary>
    public void RecordSuccess(string key)
    {
        lock (_gate)
        {
            _state.Remove(key);
        }
    }
}
