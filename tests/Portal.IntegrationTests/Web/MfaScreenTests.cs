using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Portal.Framework.Web.Authentication;
using Portal.Security.Authentication;
using Portal.Security.Mfa;
using Portal.Security.Passwords;
using Portal.Web.Controllers;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>2단계 인증 화면 (설계서 5장): 대기표 쿠키, 첫 등록 QR, 코드 확인 후 세션 발급.</summary>
public sealed class MfaScreenTests : IClassFixture<PortalWebFactory>
{
    private const string Password = "Correct#Pass1";
    private static readonly PasswordHash Hash = new Pbkdf2PasswordHasher().Hash(Password);
    private readonly PortalWebFactory _web;
    private readonly byte[] _enrolledSecret = Totp.NewSecret();

    public MfaScreenTests(PortalWebFactory web)
    {
        _web = web;
        var secrets = web.Services.GetRequiredService<IMfaSecretProtector>();
        _web.Sp
            .Returns("FW.USP_AUTH_GET_USER", p => (string)p["P_LOGIN_USER_ID"]! switch
            {
                "ptn.new" => User("ptn.new", null),
                "ptn.old" => User("ptn.old", secrets.Protect(_enrolledSecret)),
                _ => null,
            })
            .Returns("FW.USP_AUTH_LOGIN_SUCCESS_EXEC", new LoginSession { ExpireDt = DateTime.UtcNow.AddMinutes(30) })
            .Returns("FW.USP_AUTH_LOGIN_FAIL_EXEC", new LoginFailResult());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Now(byte[] secret) => Totp.Code(secret, Totp.StepOf(DateTimeOffset.UtcNow));

    [Fact]
    public async Task 협력사로그인_세션없이_대기표쿠키와_코드화면으로()
    {
        var response = await PostLoginAsync("ptn.old", "/test-screen/page");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Mfa?returnUrl=%2Ftest-screen%2Fpage", response.Headers.Location!.OriginalString);
        Assert.Null(SetCookie(response, SessionCookie.Name));
        var cookie = SetCookie(response, AccountController.MfaCookie)!.ToLowerInvariant();
        Assert.Contains("secure", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.Ordinal);
        Assert.Contains("samesite=strict", cookie, StringComparison.Ordinal);
        Assert.Contains("max-age=300", cookie, StringComparison.Ordinal);
        Assert.Null(Ticket(response)!.PendingSecret);
    }

    [Fact]
    public async Task 대기표없이_코드화면은_로그인으로()
    {
        var response = await _web.NewClient().GetAsync("/Account/Mfa", Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login?reason=AUTH.MFA_EXPIRED", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task 첫등록_QR과등록키를보여주고_코드가맞으면_세션발급()
    {
        var login = await PostLoginAsync("ptn.new", "/test-screen/page");
        var ticketCookie = CookiePair(login);
        var secret = Ticket(login)!.PendingSecret!;

        var page = await _web.NewClient(otherCookie: ticketCookie).GetAsync("/Account/Mfa?returnUrl=%2Ftest-screen%2Fpage", Ct);
        var html = await page.Content.ReadAsStringAsync(Ct);
        Assert.Contains("<svg", html, StringComparison.Ordinal);
        Assert.Contains(string.Join(' ', Totp.ToBase32(secret).Chunk(4).Select(c => new string(c))), html, StringComparison.Ordinal);
        Assert.Contains("otpauth://totp/", html, StringComparison.Ordinal);

        var response = await PostCodeAsync(ticketCookie, Now(secret), "/test-screen/page");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/test-screen/page", response.Headers.Location!.OriginalString);
        Assert.NotNull(SetCookie(response, SessionCookie.Name));
        Assert.Contains("expires=Thu, 01 Jan 1970", SetCookie(response, AccountController.MfaCookie), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ptn.new", _web.Sp.LastCall(AuthSp.MfaSave).Parameters["P_LOGIN_USER_ID"]);
    }

    [Fact]
    public async Task 등록한계정_화면에키를보여주지않고_틀린코드는다시입력()
    {
        var login = await PostLoginAsync("ptn.old", null);
        var ticketCookie = CookiePair(login);

        var page = await (await _web.NewClient(otherCookie: ticketCookie).GetAsync("/Account/Mfa", Ct)).Content.ReadAsStringAsync(Ct);
        var wrong = await PostCodeAsync(ticketCookie, Now(_enrolledSecret) == "000000" ? "111111" : "000000", null);
        var right = await PostCodeAsync(ticketCookie, Now(_enrolledSecret), null);

        Assert.DoesNotContain("<svg", page, StringComparison.Ordinal);
        Assert.DoesNotContain("otpauth://", page, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Null(SetCookie(wrong, SessionCookie.Name));
        Assert.Contains("AUTH.MFA_INVALID", await wrong.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Contains(_web.Sp.Calls, c => c.SpName == "FW.USP_AUTH_LOGIN_FAIL_EXEC" && (string?)c.Parameters["P_LOGIN_RESULT_CD"] == LoginResultCodes.FailMfa);
        Assert.Equal("/", right.Headers.Location!.OriginalString);
        Assert.NotNull(SetCookie(right, SessionCookie.Name));
    }

    [Fact]
    public async Task 대기표없이_코드를보내면_로그인화면()
    {
        var response = await PostCodeAsync(null, "123456", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(SetCookie(response, SessionCookie.Name));
        Assert.Contains("autocomplete=\"current-password\"", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    private MfaTicket? Ticket(HttpResponseMessage response) =>
        _web.Services.GetRequiredService<MfaTicketProtector>().Unprotect(CookiePair(response).Split('=', 2)[1]);

    private static string CookiePair(HttpResponseMessage response) => SetCookie(response, AccountController.MfaCookie)!.Split(';')[0];

    private async Task<HttpResponseMessage> PostLoginAsync(string userId, string? returnUrl)
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(), "/Account/Login");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
        {
            Content = new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", csrf),
                new("UserId", userId),
                new("Password", Password),
                new("ReturnUrl", returnUrl),
            ]),
        };
        return await _web.NewClient(otherCookie: cookie).SendAsync(request, Ct);
    }

    private async Task<HttpResponseMessage> PostCodeAsync(string? ticketCookie, string code, string? returnUrl)
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(), "/Account/Login");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Mfa")
        {
            Content = new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", csrf),
                new("Code", code),
                new("ReturnUrl", returnUrl),
            ]),
        };
        return await _web.NewClient(otherCookie: ticketCookie is null ? cookie : $"{cookie}; {ticketCookie}").SendAsync(request, Ct);
    }

    private static AuthUser User(string userId, byte[]? mfaSecretEnc) => new()
    {
        UserId = userId,
        CompanyCd = "P0001",
        UserTypeCd = "PARTNER",
        LangCd = "ko",
        PwdHash = Hash.Hash,
        PwdSalt = Hash.Salt,
        PwdChgDt = DateTime.UtcNow,
        PwdChgReqYn = "N",
        AccountStat = "OK",
        MfaSecretEnc = mfaSecretEnc,
    };
}
