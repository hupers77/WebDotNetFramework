using System.Net;
using Portal.Framework.Web.Authentication;
using Portal.Security.Authentication;
using Portal.Security.Passwords;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>로그인·로그아웃 화면 (설계서 5장): 세션 쿠키 속성, 오픈 리디렉트 차단, 실패 기록, 비밀번호 변경 강제.</summary>
public sealed class LoginScreenTests : IClassFixture<PortalWebFactory>
{
    private const string Password = "Correct#Pass1";
    private static readonly PasswordHash Hash = new Pbkdf2PasswordHasher().Hash(Password);
    private readonly PortalWebFactory _web;

    public LoginScreenTests(PortalWebFactory web)
    {
        _web = web;
        _web.Sp
            .Returns("FW.USP_AUTH_GET_USER", p => (string)p["P_LOGIN_USER_ID"]! switch
            {
                "hong" => User("hong", "N"),
                "first" => User("first", "Y"),
                _ => null,
            })
            .Returns("FW.USP_AUTH_LOGIN_SUCCESS_EXEC", new LoginSession { ExpireDt = DateTime.UtcNow.AddMinutes(30) })
            .Returns("FW.USP_AUTH_LOGIN_FAIL_EXEC", new LoginFailResult());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task 로그인화면_비로그인으로열림()
    {
        var response = await _web.NewClient().GetAsync("/Account/Login", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("autocomplete=\"current-password\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 로그인화면_주소로넘어온사유는정해진키만표시()
    {
        var response = await _web.NewClient().GetAsync("/Account/Login?reason=%3Cscript%3E", Ct);

        var html = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("role=\"alert\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 로그인성공_보안쿠키발급_돌아갈주소로()
    {
        var response = await PostLoginAsync("hong", Password, "/test-screen/page");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/test-screen/page", response.Headers.Location!.OriginalString);
        var cookie = SetCookie(response, SessionCookie.Name)!.ToLowerInvariant();
        Assert.Contains("secure", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.Ordinal);
        Assert.Contains("samesite=lax", cookie, StringComparison.Ordinal);
        Assert.Contains("path=/", cookie, StringComparison.Ordinal);
        Assert.DoesNotContain("expires", cookie, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example/")]
    public async Task 로그인성공_외부주소로는보내지않음(string returnUrl)
    {
        var response = await PostLoginAsync("hong", Password, returnUrl);

        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task 로그인성공_변경필요계정은비밀번호변경화면으로()
    {
        var response = await PostLoginAsync("first", Password, "/test-screen/page");

        Assert.Equal("/My/Password", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task 로그인실패_같은메시지_실패기록_쿠키없음()
    {
        var wrong = await PostLoginAsync("hong", "Wrong#Pass1", null);
        var unknown = await PostLoginAsync("nobody", Password, null);

        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Null(SetCookie(wrong, SessionCookie.Name));
        var wrongHtml = await wrong.Content.ReadAsStringAsync(Ct);
        var unknownHtml = await unknown.Content.ReadAsStringAsync(Ct);
        Assert.Contains("ID 또는 비밀번호가 올바르지 않습니다.", wrongHtml, StringComparison.Ordinal);
        Assert.Contains("ID 또는 비밀번호가 올바르지 않습니다.", unknownHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Wrong#Pass1", wrongHtml, StringComparison.Ordinal);
        Assert.Contains(_web.Sp.Calls, c => c.SpName == "FW.USP_AUTH_LOGIN_FAIL_EXEC" && (string?)c.Parameters["P_LOGIN_USER_ID"] == "hong");
    }

    [Fact]
    public async Task 로그아웃_세션종료_쿠키삭제()
    {
        var token = _web.SignIn("leaving", ["DSH_HOME:READ"]);
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(token), "/");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Logout")
        {
            Content = new FormUrlEncodedContent([new("__RequestVerificationToken", csrf)]),
        };
        var response = await _web.NewClient(token, cookie).SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(SessionToken.Hash(token), _web.Sp.LastCall("FW.USP_AUTH_LOGOUT_EXEC").Parameters["P_SESSION_HASH"]);
        Assert.Contains("expires=Thu, 01 Jan 1970", SetCookie(response, SessionCookie.Name), StringComparison.OrdinalIgnoreCase);
    }

    private async Task<HttpResponseMessage> PostLoginAsync(string userId, string password, string? returnUrl)
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(), "/Account/Login");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
        {
            Content = new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", csrf),
                new("UserId", userId),
                new("Password", password),
                new("ReturnUrl", returnUrl),
            ]),
        };
        return await _web.NewClient(otherCookie: cookie).SendAsync(request, Ct);
    }

    private static AuthUser User(string userId, string pwdChgReqYn) => new()
    {
        UserId = userId,
        CompanyCd = "HQ",
        UserTypeCd = "EMP",
        LangCd = "ko",
        PwdHash = Hash.Hash,
        PwdSalt = Hash.Salt,
        PwdChgDt = DateTime.UtcNow,
        PwdChgReqYn = pwdChgReqYn,
        AccountStat = "OK",
    };
}

/// <summary>로그인 Rate Limit (설정 <c>AUTH.LOGIN_RATE_PER_MIN</c>). 다른 테스트와 한도를 나누지 않도록 호스트를 따로 띄운다.</summary>
public sealed class LoginRateLimitTests : IDisposable
{
    private readonly PortalWebFactory _web = new();

    public LoginRateLimitTests() => _web.Config.Set("AUTH.LOGIN_RATE_PER_MIN", "2");

    [Fact]
    public async Task 한도초과_429()
    {
        var client = _web.NewClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var content = new FormUrlEncodedContent([new("UserId", "x"), new("Password", "y")]);
            statuses.Add((await client.PostAsync("/Account/Login", content, TestContext.Current.CancellationToken)).StatusCode);
        }

        Assert.NotEqual(HttpStatusCode.TooManyRequests, statuses[1]);
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[2]);
    }

    public void Dispose() => _web.Dispose();
}
