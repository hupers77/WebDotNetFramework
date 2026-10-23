using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Portal.Core.Keys;
using Portal.Framework.Web.Api;
using Portal.Security.Passwords;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>
/// 외부 API 관문: 토큰 발급(Client Credentials), Bearer 확인, 클라이언트 사용 여부·허용 IP·허용 API·호출 한도, 오류 형식, OpenAPI 문서, 기록(민감 값 제외).
/// 클라이언트 ID는 테스트마다 달라야 한다 (클라이언트 설정은 잠깐 기억된다).
/// </summary>
public sealed class ApiGatewayTests : IClassFixture<PortalWebFactory>
{
    private const string Secret = "s3cret-value-for-test";
    private static readonly Pbkdf2PasswordHasher Hasher = new();
    private static readonly PasswordHash SecretHash = Hasher.Hash(Secret);
    private readonly PortalWebFactory _web;

    public ApiGatewayTests(PortalWebFactory web)
    {
        _web = web;
        web.Config.Set("API.TOKEN_MINUTES", "30").Set("API.DEFAULT_RATE_PER_MIN", "600");
        web.Sp.Returns(ApiClientStore.GetSp, p => Clients.GetValueOrDefault((string)p["P_CLIENT_ID"]!));
    }

    private static readonly Dictionary<string, ApiClientStore.ApiClientRow> Clients = new(StringComparer.Ordinal);

    private static string AddClient(string id, string codes = "PING", string activeYn = "Y", string? ips = null, int? rate = null, bool withSecret = true)
    {
        lock (Clients)
        {
            Clients[id] = new ApiClientStore.ApiClientRow
            {
                ClientId = id, ClientNm = id, ApiCodes = codes, ActiveYn = activeYn, AllowIpList = ips, RatePerMin = rate,
                SecretHash = withSecret ? SecretHash.Hash : null, SecretSalt = withSecret ? SecretHash.Salt : null,
            };
        }

        return id;
    }

    private static HttpRequestMessage TokenRequest(string clientId, string secret = Secret) =>
        new(HttpMethod.Post, "/api/v1/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = secret,
            }),
        };

    private static async Task<string> GetTokenAsync(HttpClient client, string clientId)
    {
        var response = await client.SendAsync(TokenRequest(clientId), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("access_token").GetString()!;
    }

    private static HttpRequestMessage Get(string url, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    [Fact]
    public async Task 토큰발급_성공은_Bearer토큰과_유효시간_캐시금지()
    {
        var id = AddClient("tok-ok");
        using var client = _web.NewClient();

        var response = await client.SendAsync(TokenRequest(id), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        var json = await JsonAsync(response);
        Assert.Equal("Bearer", json.GetProperty("token_type").GetString());
        Assert.Equal(1800, json.GetProperty("expires_in").GetInt32());
        Assert.Equal(3, json.GetProperty("access_token").GetString()!.Split('.').Length);
    }

    [Fact]
    public async Task 토큰발급은_CSRF토큰없이도_된다_Basic인증도_허용()
    {
        var id = AddClient("tok-basic");
        using var client = _web.NewClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{id}:{Secret}")));

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task 비밀값_틀림_없는ID_미발급_사용중지는_모두_같은_invalid_client()
    {
        AddClient("tok-bad");
        AddClient("tok-nosecret", withSecret: false);
        AddClient("tok-off", activeYn: "N");
        using var client = _web.NewClient();

        string[] bodies = new string[4];
        var cases = new[] { ("tok-bad", "wrong-secret"), ("no-such-client", Secret), ("tok-nosecret", Secret), ("tok-off", Secret) };
        for (var i = 0; i < cases.Length; i++)
        {
            var response = await client.SendAsync(TokenRequest(cases[i].Item1, cases[i].Item2), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            bodies[i] = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        }

        Assert.All(bodies, b => Assert.Equal(bodies[0], b));
        Assert.Contains("invalid_client", bodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task 토큰요청_형식오류는_400()
    {
        using var client = _web.NewClient();

        var wrongGrant = new HttpRequestMessage(HttpMethod.Post, "/api/v1/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "password", ["client_id"] = "x", ["client_secret"] = "y" }),
        };
        var response1 = await client.SendAsync(wrongGrant, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response1.StatusCode);
        Assert.Equal("unsupported_grant_type", (await JsonAsync(response1)).GetProperty("error").GetString());

        var missing = new HttpRequestMessage(HttpMethod.Post, "/api/v1/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" }),
        };
        var response2 = await client.SendAsync(missing, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response2.StatusCode);

        var json = new HttpRequestMessage(HttpMethod.Post, "/api/v1/token") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        var response3 = await client.SendAsync(json, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response3.StatusCode);
    }

    [Fact]
    public async Task ping은_토큰이_있어야_하고_호출자를_알려준다()
    {
        var id = AddClient("ping-ok");
        using var client = _web.NewClient();
        var token = await GetTokenAsync(client, id);

        var ok = await client.SendAsync(Get("/api/v1/ping", token), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(id, (await JsonAsync(ok)).GetProperty("clientId").GetString());

        var none = await client.SendAsync(Get("/api/v1/ping", null), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Contains("Bearer", none.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
        Assert.Equal("invalid_token", (await JsonAsync(none)).GetProperty("error").GetProperty("code").GetString());

        var garbage = await client.SendAsync(Get("/api/v1/ping", "a.b.c"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, garbage.StatusCode);
    }

    [Fact]
    public async Task 화면_세션쿠키로는_API를_부를수없다()
    {
        var session = _web.SignIn("api-cookie-user", ["DSH_HOME:READ"]);
        using var client = _web.NewClient(session);

        var response = await client.SendAsync(Get("/api/v1/ping", null), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task 허용되지_않은_API는_403_insufficient_scope()
    {
        var id = AddClient("scope-none", codes: "OTHER");
        using var client = _web.NewClient();
        var token = await GetTokenAsync(client, id);

        var response = await client.SendAsync(Get("/api/v1/ping", token), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("insufficient_scope", (await JsonAsync(response)).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task 허용_IP가_맞지않으면_토큰발급도_호출도_403()
    {
        var id = AddClient("ip-limited", ips: "10.99.0.0/16");
        using var client = _web.NewClient();

        var response = await client.SendAsync(TokenRequest(id), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("access_denied", (await JsonAsync(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task 저장된_IP목록이_깨져있으면_안전하게_거부()
    {
        var id = AddClient("ip-broken", ips: "not-an-ip");
        using var client = _web.NewClient();

        var response = await client.SendAsync(TokenRequest(id), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 분당_호출한도를_넘으면_429와_Retry_After()
    {
        var id = AddClient("rate-low", rate: 2);
        using var client = _web.NewClient();
        var token = await GetTokenAsync(client, id);

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 3; i++)
        {
            last = await client.SendAsync(Get("/api/v1/ping", token), TestContext.Current.CancellationToken);
            statuses.Add(last.StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        Assert.NotNull(last!.Headers.RetryAfter);
    }

    [Fact]
    public async Task 없는_API는_404_JSON()
    {
        var id = AddClient("nf-client");
        using var client = _web.NewClient();
        var token = await GetTokenAsync(client, id);

        var response = await client.SendAsync(Get("/api/v1/nothing", token), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", (await JsonAsync(response)).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task OpenAPI_문서는_토큰없이_등록된_API를_보여준다()
    {
        using var client = _web.NewClient();

        var response = await client.SendAsync(Get("/api/v1/openapi.json", null), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await JsonAsync(response);
        Assert.Equal("3.0.3", json.GetProperty("openapi").GetString());
        var paths = json.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/token", out var token));
        Assert.True(token.GetProperty("post").TryGetProperty("requestBody", out _));
        var ping = paths.GetProperty("/api/v1/ping").GetProperty("get");
        Assert.Equal("PING", ping.GetProperty("x-api-code").GetString());
        Assert.True(ping.TryGetProperty("security", out _));
    }

    [Fact]
    public async Task 기록은_클라이언트와_상태를_남기고_토큰요청_본문과_비밀값은_남기지않는다()
    {
        var id = AddClient("log-client");
        using var client = _web.NewClient();
        var buffer = _web.Services.GetService(typeof(ApiLogBuffer)) as ApiLogBuffer;
        Assert.NotNull(buffer);
        while (buffer.Reader.TryRead(out _))
        {
        }

        var token = await GetTokenAsync(client, id);
        await client.SendAsync(Get("/api/v1/ping?access_token=zzz&x=1", token), TestContext.Current.CancellationToken);

        var entries = new List<ApiLogEntry>();
        while (buffer.Reader.TryRead(out var e))
        {
            entries.Add(e);
        }

        var tokenLog = Assert.Single(entries, e => e.Url == "/api/v1/token");
        Assert.Equal("IN", tokenLog.DirectionCd);
        Assert.Equal((short)200, tokenLog.StatusCode);
        Assert.Null(tokenLog.RequestBody);
        Assert.Null(tokenLog.ResponseBody);

        var pingLog = Assert.Single(entries, e => e.Url.StartsWith("/api/v1/ping", StringComparison.Ordinal));
        Assert.Equal(id, pingLog.ClientId);
        Assert.Equal("/api/v1/ping?access_token=***&x=1", pingLog.Url);
        Assert.Equal("GET", pingLog.HttpMethod);
        Assert.DoesNotContain(token, pingLog.ResponseBody ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, string.Join('|', entries.Select(e => e.RequestBody + e.ResponseBody + e.Url)), StringComparison.Ordinal);
    }
}
