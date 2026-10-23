using System.Text;
using Microsoft.Extensions.Hosting;
using Portal.Framework.Web.Api;
using Portal.Testing;

namespace Portal.UnitTests.Api;

public sealed class ApiJwtTests
{
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private readonly FakeTimeProvider _time = new();

    private ApiJwt NewJwt(byte[]? key = null, string issuer = "portal", string audience = "portal-api") => new(key ?? Key, issuer, audience, _time);

    [Fact]
    public void 만든토큰은_검증되고_클라이언트와허용코드가나온다()
    {
        var jwt = NewJwt();
        var token = jwt.Create("erp-sync", ["PING", "ORDER_GET"], TimeSpan.FromMinutes(30), out var expires);

        var caller = jwt.Validate(token);

        Assert.NotNull(caller);
        Assert.Equal("erp-sync", caller.ClientId);
        Assert.Equal(["ORDER_GET", "PING"], caller.ApiCodes.Order().ToArray());
        Assert.Equal(_time.GetUtcNow().AddMinutes(30), expires);
    }

    [Fact]
    public void 만료되면_거부하되_시계오차30초는_허용()
    {
        var jwt = NewJwt();
        var token = jwt.Create("c", ["PING"], TimeSpan.FromMinutes(1), out _);

        _time.Advance(TimeSpan.FromSeconds(60 + 29));
        Assert.NotNull(jwt.Validate(token));

        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(jwt.Validate(token));
    }

    [Fact]
    public void 내용을_바꾸거나_다른키로_서명하면_거부()
    {
        var jwt = NewJwt();
        var token = jwt.Create("c", ["PING"], TimeSpan.FromMinutes(5), out _);
        var parts = token.Split('.');

        // payload의 scope를 넓힌 토큰 (서명은 그대로)
        var forgedPayload = ApiJwt.Base64Url(Encoding.UTF8.GetBytes("""{"iss":"portal","aud":"portal-api","sub":"c","scope":"PING ADMIN","exp":4102444800}"""));
        Assert.Null(jwt.Validate($"{parts[0]}.{forgedPayload}.{parts[2]}"));

        var otherKey = NewJwt(Enumerable.Range(50, 32).Select(i => (byte)i).ToArray());
        Assert.Null(otherKey.Validate(token));
    }

    [Fact]
    public void 알고리즘_none_이나_다른헤더는_거부()
    {
        var jwt = NewJwt();
        var header = ApiJwt.Base64Url(Encoding.ASCII.GetBytes("""{"alg":"none","typ":"JWT"}"""));
        var payload = ApiJwt.Base64Url(Encoding.UTF8.GetBytes("""{"iss":"portal","aud":"portal-api","sub":"c","scope":"PING","exp":4102444800}"""));

        Assert.Null(jwt.Validate($"{header}.{payload}."));
        Assert.Null(jwt.Validate($"{header}.{payload}.abc"));
    }

    [Fact]
    public void 발급자나_대상이_다르면_거부()
    {
        var token = NewJwt(issuer: "other").Create("c", ["PING"], TimeSpan.FromMinutes(5), out _);
        Assert.Null(NewJwt().Validate(token));

        var token2 = NewJwt(audience: "other-api").Create("c", ["PING"], TimeSpan.FromMinutes(5), out _);
        Assert.Null(NewJwt().Validate(token2));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("a.b")]
    [InlineData("a.b.c.d")]
    [InlineData("!!!.@@@.###")]
    public void 형식이_깨진_토큰은_예외없이_거부(string? token) => Assert.Null(NewJwt().Validate(token));

    [Fact]
    public void 너무_긴_토큰은_거부() => Assert.Null(NewJwt().Validate(new string('a', 5000)));

    [Fact]
    public void 서명키_없으면_운영은_실패하고_개발은_임의키()
    {
        var none = new ApiOptions();
        Assert.Throws<InvalidOperationException>(() => PortalApiExtensions.ResolveSigningKey(none, Env("Production")));
        Assert.Equal(32, PortalApiExtensions.ResolveSigningKey(none, Env("Development")).Length);
        Assert.NotEqual(PortalApiExtensions.ResolveSigningKey(none, Env("Testing")), PortalApiExtensions.ResolveSigningKey(none, Env("Testing")));
    }

    [Fact]
    public void 서명키는_Base64이고_32바이트이상()
    {
        var env = Env("Production");
        Assert.Throws<InvalidOperationException>(() => PortalApiExtensions.ResolveSigningKey(new ApiOptions { SigningKey = "not base64!" }, env));
        Assert.Throws<InvalidOperationException>(() => PortalApiExtensions.ResolveSigningKey(new ApiOptions { SigningKey = Convert.ToBase64String(new byte[31]) }, env));
        Assert.Equal(Key, PortalApiExtensions.ResolveSigningKey(new ApiOptions { SigningKey = Convert.ToBase64String(Key) }, env));
    }

    private static FakeEnv Env(string name) => new(name);

    private sealed class FakeEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "t";

        public string ContentRootPath { get; set; } = "/";

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
