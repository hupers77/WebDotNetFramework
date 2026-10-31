using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Portal.Core.Security;
using Portal.Framework.Web.Api;
using Portal.Framework.Web.Api.Outbound;
using Portal.Testing;

namespace Portal.UnitTests.Api;

public sealed class OutboundAddressPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("169.254.169.254")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("64:ff9b::7f00:1")]
    [InlineData("64:ff9b::a9fe:a9fe")]
    [InlineData("2002:7f00:1::1")]
    public void 루프백_링크로컬_멀티캐스트는_사내망허용이어도_막는다(string ip)
    {
        Assert.False(OutboundAddressPolicy.IsAllowed(IPAddress.Parse(ip), allowPrivate: false));
        Assert.False(OutboundAddressPolicy.IsAllowed(IPAddress.Parse(ip), allowPrivate: true));
    }

    [Theory]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.0.10")]
    [InlineData("100.64.0.1")]
    [InlineData("fd00::1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("64:ff9b::a00:1")]
    [InlineData("2002:c0a8:1::1")]
    [InlineData("198.18.0.1")]
    [InlineData("192.0.0.8")]
    public void 사설_대역은_사내망허용일때만(string ip)
    {
        Assert.False(OutboundAddressPolicy.IsAllowed(IPAddress.Parse(ip), allowPrivate: false));
        Assert.True(OutboundAddressPolicy.IsAllowed(IPAddress.Parse(ip), allowPrivate: true));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700::1111")]
    public void 공인_주소는_항상_허용(string ip)
    {
        Assert.True(OutboundAddressPolicy.IsAllowed(IPAddress.Parse(ip), allowPrivate: false));
    }
}

public sealed class OutboundCircuitBreakerTests
{
    [Fact]
    public void 연속_실패가_임계값이면_차단하고_시간이_지나면_한번_시도_성공하면_풀린다()
    {
        var time = new FakeTimeProvider();
        var breaker = new OutboundCircuitBreaker(time);
        for (var i = 0; i < OutboundCircuitBreaker.Threshold - 1; i++)
        {
            breaker.RecordFailure("T");
        }

        Assert.False(breaker.IsOpen("T"));
        breaker.RecordFailure("T");
        Assert.True(breaker.IsOpen("T"));
        Assert.False(breaker.IsOpen("OTHER"));

        time.Advance(TimeSpan.FromSeconds(OutboundCircuitBreaker.OpenSeconds + 1));
        Assert.False(breaker.IsOpen("T"));

        // 시험 호출이 실패하면 바로 다시 차단
        breaker.RecordFailure("T");
        Assert.True(breaker.IsOpen("T"));

        time.Advance(TimeSpan.FromSeconds(OutboundCircuitBreaker.OpenSeconds + 1));
        breaker.RecordSuccess("T");
        breaker.RecordFailure("T");
        Assert.False(breaker.IsOpen("T"));
    }
}

public sealed class OutboundApiClientTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakeTargets _targets = new();
    private readonly FakeHandler _handler = new();
    private readonly ApiLogBuffer _logs = new();
    private readonly OutboundCircuitBreaker _breaker;
    private readonly OutboundApiClient _client;

    public OutboundApiClientTests()
    {
        _breaker = new OutboundCircuitBreaker(_time);
        _client = new OutboundApiClient(_targets, new FakeFactory(_handler), new PlainProtector(), _logs, _breaker, new InstantTime(_time));
        _targets.Set(Target());
    }

    public void Dispose() => _handler.Dispose();

    private static OutboundTarget Target(string auth = "NONE", string? header = null, string? user = null, string? secret = null, int retry = 2, bool active = true, string url = "https://erp.example.com/api") =>
        new("ERP", url, auth, header, user, secret is null ? null : "enc:" + secret, 5, retry, false, active);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OutboundRequest Get(string path = "/orders") => new("ERP", HttpMethod.Get, path);

    [Fact]
    public async Task 기본주소_뒤에_경로를_붙여_보내고_응답을_돌려준다_기록은_OUT()
    {
        _handler.Enqueue(HttpStatusCode.OK, """{"ok":true}""");

        var response = await _client.SendAsync(Get("/orders?id=7"), Ct);

        Assert.True(response.IsSuccess);
        Assert.Equal("""{"ok":true}""", response.Body);
        Assert.Equal("https://erp.example.com/api/orders?id=7", _handler.Requests.Single().Uri);
        Assert.True(_logs.Reader.TryRead(out var log));
        Assert.Equal("OUT", log.DirectionCd);
        Assert.Equal("ERP", log.ClientId);
        Assert.Equal((short)200, log.StatusCode);
    }

    [Fact]
    public async Task 인증헤더는_방식별로_붙고_기록에는_비밀값이_없다()
    {
        _targets.Set(Target("BASIC", user: "svc", secret: "pw!"));
        _handler.Enqueue(HttpStatusCode.OK, "{}");
        await _client.SendAsync(Get(), Ct);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("svc:pw!")), _handler.Requests.Last().Authorization);

        _targets.Set(Target("BEARER", secret: "tok123"));
        _handler.Enqueue(HttpStatusCode.OK, "{}");
        await _client.SendAsync(Get(), Ct);
        Assert.Equal("Bearer tok123", _handler.Requests.Last().Authorization);

        _targets.Set(Target("API_KEY", header: "X-Api-Key", secret: "key-1"));
        _handler.Enqueue(HttpStatusCode.OK, "{}");
        await _client.SendAsync(Get(), Ct);
        Assert.Equal("key-1", _handler.Requests.Last().ApiKey);

        var logged = new List<string>();
        while (_logs.Reader.TryRead(out var e))
        {
            logged.Add(e.Url + e.RequestBody + e.ResponseBody);
        }

        Assert.DoesNotContain(logged, l => l.Contains("tok123", StringComparison.Ordinal) || l.Contains("key-1", StringComparison.Ordinal) || l.Contains("pw!", StringComparison.Ordinal));
    }

    [Fact]
    public async Task 요청과_응답_본문의_민감필드는_가려서_기록한다()
    {
        _handler.Enqueue(HttpStatusCode.OK, """{"access_token":"zzz","name":"a"}""");

        await _client.SendAsync(new OutboundRequest("ERP", HttpMethod.Post, "/login", """{"password":"p1","id":"x"}"""), Ct);

        Assert.True(_logs.Reader.TryRead(out var log));
        Assert.DoesNotContain("p1", log.RequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("zzz", log.ResponseBody, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"x\"", log.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GET은_5xx면_재시도하고_성공하면_멈춘다()
    {
        _handler.Enqueue(HttpStatusCode.BadGateway, "x");
        _handler.Enqueue(HttpStatusCode.ServiceUnavailable, "x");
        _handler.Enqueue(HttpStatusCode.OK, "fine");

        var response = await _client.SendAsync(Get(), Ct);

        Assert.Equal(3, response.Attempts);
        Assert.Equal("fine", response.Body);
        Assert.Equal(3, _handler.Requests.Count);
    }

    [Fact]
    public async Task POST는_멱등표시가_없으면_재시도하지_않는다()
    {
        _handler.Enqueue(HttpStatusCode.BadGateway, "x");
        _handler.Enqueue(HttpStatusCode.OK, "late");

        var first = await _client.SendAsync(new OutboundRequest("ERP", HttpMethod.Post, "/orders", "{}"), Ct);
        Assert.Equal(502, first.StatusCode);
        Assert.Single(_handler.Requests);

        var second = await _client.SendAsync(new OutboundRequest("ERP", HttpMethod.Post, "/orders", "{}", Idempotent: true), Ct);
        Assert.Equal(200, second.StatusCode);
    }

    [Fact]
    public async Task 클라이언트오류_4xx는_재시도하지_않고_그대로_돌려준다_연속_실패에_넣지_않는다()
    {
        for (var i = 0; i < OutboundCircuitBreaker.Threshold + 1; i++)
        {
            _handler.Enqueue(HttpStatusCode.NotFound, "no");
            Assert.Equal(404, (await _client.SendAsync(Get(), Ct)).StatusCode);
        }

        Assert.False(_breaker.IsOpen("ERP"));
        Assert.Equal(OutboundCircuitBreaker.Threshold + 1, _handler.Requests.Count);
    }

    [Fact]
    public async Task 네트워크_오류가_계속되면_예외_그리고_연속_실패가_쌓이면_차단()
    {
        _targets.Set(Target(retry: 0));
        for (var i = 0; i < OutboundCircuitBreaker.Threshold; i++)
        {
            _handler.EnqueueFailure();
            var ex = await Assert.ThrowsAsync<OutboundApiException>(() => _client.SendAsync(Get(), Ct));
            Assert.Equal(OutboundFailure.Network, ex.Failure);
        }

        var blocked = await Assert.ThrowsAsync<OutboundApiException>(() => _client.SendAsync(Get(), Ct));
        Assert.Equal(OutboundFailure.CircuitOpen, blocked.Failure);
        Assert.Equal(OutboundCircuitBreaker.Threshold, _handler.Requests.Count);
    }

    [Fact]
    public async Task 없는_대상_사용중지_대상은_거부()
    {
        var unknown = await Assert.ThrowsAsync<OutboundApiException>(() => _client.SendAsync(new OutboundRequest("NOPE", HttpMethod.Get, "/"), Ct));
        _targets.Set(Target(active: false));
        var off = await Assert.ThrowsAsync<OutboundApiException>(() => _client.SendAsync(Get(), Ct));

        Assert.Equal(OutboundFailure.UnknownTarget, unknown.Failure);
        Assert.Equal(OutboundFailure.Disabled, off.Failure);
        Assert.Empty(_handler.Requests);
    }

    [Theory]
    [InlineData("https://evil.example.com/x")]
    [InlineData("//evil.example.com/x")]
    [InlineData("/../admin")]
    [InlineData("/a\\b")]
    [InlineData("/a\nb")]
    public void 대상_밖으로_나가는_경로는_거부(string path)
    {
        var ex = Assert.Throws<OutboundApiException>(() => OutboundApiClient.BuildUri(Target(), path));

        Assert.Equal(OutboundFailure.InvalidPath, ex.Failure);
    }

    [Theory]
    [InlineData("", "https://erp.example.com/api")]
    [InlineData("orders", "https://erp.example.com/api/orders")]
    [InlineData("/orders", "https://erp.example.com/api/orders")]
    [InlineData("?a=1", "https://erp.example.com/api?a=1")]
    public void 경로는_기본주소_뒤에_붙는다(string path, string expected) =>
        Assert.Equal(expected, OutboundApiClient.BuildUri(Target(), path).ToString());

    [Fact]
    public async Task 복호화할_수_없는_비밀값은_안내_예외()
    {
        _targets.Set(new OutboundTarget("ERP", "https://erp.example.com", "BEARER", null, null, "corrupt", 5, 0, false, true));

        var ex = await Assert.ThrowsAsync<OutboundApiException>(() => _client.SendAsync(Get(), Ct));

        Assert.Equal(OutboundFailure.SecretUnreadable, ex.Failure);
    }

    [Fact]
    public async Task 큰_응답은_한도까지만_읽는다()
    {
        _handler.Enqueue(HttpStatusCode.OK, new string('a', OutboundApiClient.MaxResponseBytes + 100));

        var response = await _client.SendAsync(Get(), Ct);

        Assert.True(response.Truncated);
        Assert.Equal(OutboundApiClient.MaxResponseBytes, response.Body.Length);
    }

    private sealed record Seen(string Uri, string? Authorization, string? ApiKey);

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();

        public List<Seen> Requests { get; } = [];

        public void Enqueue(HttpStatusCode status, string body) =>
            _responses.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

        public void EnqueueFailure() => _responses.Enqueue(() => throw new HttpRequestException("boom"));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new Seen(request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-Api-Key", out var v) ? v.Single() : null));
            return Task.FromResult(_responses.Dequeue()());
        }
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeTargets : IOutboundTargetStore
    {
        private readonly Dictionary<string, OutboundTarget> _all = new(StringComparer.Ordinal);

        public void Set(OutboundTarget target) => _all[target.TargetKey] = target;

        public Task<OutboundTarget?> GetAsync(string targetKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(_all.GetValueOrDefault(targetKey));
    }

    private sealed class PlainProtector : ISecretProtector
    {
        public string Protect(string plainText) => "enc:" + plainText;

        public string Unprotect(string protectedText) =>
            protectedText.StartsWith("enc:", StringComparison.Ordinal) ? protectedText[4..] : throw new System.Security.Cryptography.CryptographicException("bad");
    }

    /// <summary>재시도 대기를 기다리지 않는 시간 (Task.Delay가 바로 끝난다).</summary>
    private sealed class InstantTime(FakeTimeProvider inner) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

        public override long GetTimestamp() => inner.GetTimestamp();

        public override long TimestampFrequency => inner.TimestampFrequency;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            base.CreateTimer(callback, state, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }
}

public sealed class OutboundTargetStoreTests
{
    [Fact]
    public async Task 대상을_읽어_기본주소_끝의_슬래시를_빼고_잠깐_기억한다()
    {
        var sp = new FakeSpExecutor();
        var calls = 0;
        sp.Returns(OutboundTargetStore.GetSp, p =>
        {
            if ((string?)p["P_TARGET_KEY"] != "ERP")
            {
                return null;
            }

            calls++;
            return new OutboundTargetStore.Row { TargetKey = "ERP", BaseUrl = "https://erp.example.com/api/", AllowPrivateYn = "Y", UseYn = "N" };
        });
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var store = new OutboundTargetStore(sp, cache);

        var first = await store.GetAsync("ERP", TestContext.Current.CancellationToken);
        await store.GetAsync("ERP", TestContext.Current.CancellationToken);

        Assert.Equal("https://erp.example.com/api", first!.BaseUrl);
        Assert.True(first.AllowPrivate);
        Assert.False(first.Active);
        Assert.Equal(1, calls);
        Assert.Null(await store.GetAsync("NONE", TestContext.Current.CancellationToken));
    }
}
