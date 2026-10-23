using System.Net;
using Portal.Framework.Web.Authentication;
using Portal.Security.Authentication;
using Portal.Security.Passwords;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>협력사 전용 로그인 화면 (2026-10-22 요구사항): 별도 주소, 협력사 계정만 통과, 본사 계정은 비밀번호 오류와 같은 응답.</summary>
public sealed class PartnerLoginScreenTests : IClassFixture<PortalWebFactory>
{
    private const string Password = "Correct#Pass1";
    private static readonly PasswordHash Hash = new Pbkdf2PasswordHasher().Hash(Password);
    private readonly PortalWebFactory _web;

    public PartnerLoginScreenTests(PortalWebFactory web)
    {
        _web = web;
        web.Config.Set("AUTH.PARTNER_MFA", "N");
        web.Sp
            .Returns("FW.USP_AUTH_GET_USER", p => (string)p["P_LOGIN_USER_ID"]! switch
            {
                "ptn.kim" => User("ptn.kim", "PARTNER"),
                "emp.hong" => User("emp.hong", "EMP"),
                _ => null,
            })
            .Returns("FW.USP_AUTH_LOGIN_SUCCESS_EXEC", new LoginSession { ExpireDt = DateTime.UtcNow.AddMinutes(30) })
            .Returns("FW.USP_AUTH_LOGIN_FAIL_EXEC", new LoginFailResult());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task 협력사화면은_비로그인으로열리고_자기주소로보내며_가입링크와_본사로그인링크()
    {
        var html = await _web.NewClient().GetStringAsync("/Account/PartnerLogin", Ct);

        Assert.Contains("action=\"/Account/PartnerLogin\"", html, StringComparison.Ordinal);
        Assert.Contains("LOGIN.PARTNER_GUIDE", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/Account/Join\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/Account/Login\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 본사화면에서_협력사로그인으로가는링크()
    {
        var html = await _web.NewClient().GetStringAsync("/Account/Login", Ct);

        Assert.Contains("href=\"/Account/PartnerLogin\"", html, StringComparison.Ordinal);
        Assert.Contains("action=\"/Account/Login\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 협력사계정은_협력사화면에서_로그인된다()
    {
        var response = await PostAsync("ptn.kim");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(SetCookie(response, SessionCookie.Name));
    }

    [Fact]
    public async Task 본사계정은_협력사화면에서_비밀번호오류와같이거절()
    {
        var response = await PostAsync("emp.hong");
        var html = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(SetCookie(response, SessionCookie.Name));
        Assert.Contains("ID 또는 비밀번호가 올바르지 않습니다.", html, StringComparison.Ordinal);
        Assert.Contains("action=\"/Account/PartnerLogin\"", html, StringComparison.Ordinal); // 틀려도 협력사 화면에 머문다
        Assert.Contains(_web.Sp.Calls, c => c.SpName == "FW.USP_AUTH_LOGIN_FAIL_EXEC" && (string?)c.Parameters["P_LOGIN_USER_ID"] == "emp.hong");
    }

    [Fact]
    public async Task 본사화면은_기존처럼_모든계정을받는다()
    {
        var response = await PostAsync("ptn.kim", "/Account/Login");

        Assert.NotNull(SetCookie(response, SessionCookie.Name));
    }

    private async Task<HttpResponseMessage> PostAsync(string userId, string path = "/Account/PartnerLogin")
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(), "/Account/Login");
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent([new("__RequestVerificationToken", csrf), new("UserId", userId), new("Password", Password)]),
        };
        return await _web.NewClient(otherCookie: cookie).SendAsync(request, Ct);
    }

    private static AuthUser User(string userId, string type) => new()
    {
        UserId = userId,
        CompanyCd = "P0001",
        UserTypeCd = type,
        LangCd = "ko",
        PwdHash = Hash.Hash,
        PwdSalt = Hash.Salt,
        PwdChgDt = DateTime.UtcNow,
        PwdChgReqYn = "N",
        AccountStat = "OK",
    };
}
