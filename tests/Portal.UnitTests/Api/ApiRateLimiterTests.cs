using Portal.Framework.Web.Api;
using Portal.Testing;

namespace Portal.UnitTests.Api;

public sealed class ApiRateLimiterTests
{
    [Fact]
    public void 한도까지만_허용하고_다음_분에_다시_허용()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 10, TimeSpan.Zero));
        var limiter = new ApiRateLimiter(time);

        Assert.True(limiter.TryAcquire("a", 2, out _));
        Assert.True(limiter.TryAcquire("a", 2, out _));
        Assert.False(limiter.TryAcquire("a", 2, out var retry));
        Assert.Equal(50, retry);

        // 다른 키는 따로 센다
        Assert.True(limiter.TryAcquire("b", 2, out _));

        time.Advance(TimeSpan.FromSeconds(50));
        Assert.True(limiter.TryAcquire("a", 2, out _));
    }
}
