using System.Threading.Channels;

namespace Portal.Framework.Web.Logging;

/// <summary>
/// 접근 기록 대기열. 응답을 늦추지 않도록 요청 처리 중에는 넣기만 하고, <see cref="AccessLogWriter"/>가 모아서 적재한다.
/// 대기열이 가득 차면(DB 장애 등) 요청을 막지 않고 가장 오래된 기록부터 버리며, 버린 건수를 경고 로그로 남긴다.
/// </summary>
public sealed class AccessLogBuffer
{
    internal const int Capacity = 10_000;

    private readonly Channel<AccessLogEntry> _channel;
    private long _dropped;

    /// <summary>대기열을 만든다.</summary>
    public AccessLogBuffer() =>
        _channel = Channel.CreateBounded<AccessLogEntry>(
            new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            _ => Interlocked.Increment(ref _dropped));

    internal ChannelReader<AccessLogEntry> Reader => _channel.Reader;

    /// <summary>기록을 넣는다.</summary>
    public void Enqueue(AccessLogEntry entry) => _channel.Writer.TryWrite(entry);

    /// <summary>지난번 확인 이후 버린 건수를 가져오고 0으로 되돌린다.</summary>
    internal long TakeDroppedCount() => Interlocked.Exchange(ref _dropped, 0);

    /// <summary>더 받지 않는다 (종료 시).</summary>
    internal void Complete() => _channel.Writer.TryComplete();
}
