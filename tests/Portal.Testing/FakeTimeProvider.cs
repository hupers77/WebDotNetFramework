namespace Portal.Testing;

/// <summary>시각을 고정하고 직접 진행시키는 테스트용 <see cref="TimeProvider"/>.</summary>
public sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public FakeTimeProvider()
        : this(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
    {
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public override long GetTimestamp() => _now.UtcTicks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public void Advance(TimeSpan by) => _now = _now.Add(by);

    public void SetUtcNow(DateTimeOffset value) => _now = value;
}
