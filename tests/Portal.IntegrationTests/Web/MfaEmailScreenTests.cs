using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Portal.Framework.Web.Authentication;
using Portal.Security.Authentication;
using Portal.Security.Mfa;
using Portal.Security.Passwords;
using Portal.Web.Controllers;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>2단계 인증 화면의 이메일 인증번호: 허용된 계정에만 [받기] 버튼, 보내기, 번호 확인 후 세션, 틀린 번호는 다시 입력.</summary>
public sealed class MfaEmailScreenTests : IClassFixture<PortalWebFactory>
{
    private const string Password = "Correct#Pass1";
    private static readonly PasswordHash Hash = new Pbkdf2PasswordHasher().Hash(Password);
    private readonly PortalWebFactory _web;

    public MfaEmailScreenTests(PortalWebFactory web)
    {
        _web = web;
        web.Config.Set("AUTH.PARTNER_EMAIL_OTP", "Y");
        web.Sp
            .Returns("FW.USP_AUTH_GET_USER", p => (string)p["P_LOGIN_USER_ID"]! switch
            {
                "ptn.mail" => User("ptn.mail", "Y"),
                "ptn.nomail" => User("ptn.nomail", "N"),
                _ => null,
            })
            .Returns(AuthSp.OtpIssue, new OtpIssued { EmailEnc = [1, 2, 3], LangCd = "ko" })
            .Returns(AuthSp.OtpVerify, p => new OtpVerified { MatchYn = _web.Mailer.Sent.Count > 0 && ((byte[])p["P_CODE_HASH"]!).SequenceEqual(EmailOtp.Hash("ptn.mail", _web.Mailer.LastCode)) ? "Y" : "N" })
            .Returns("FW.USP_AUTH_LOGIN_SUCCESS_EXEC", new LoginSession { ExpireDt = DateTime.UtcNow.AddMinutes(30) })
            .Returns("FW.USP_AUTH_LOGIN_FAIL_EXEC", new LoginFailResult());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task 이메일이있는협력사만_받기버튼()
    {
        var withMail = await (await _web.NewClient(otherCookie: CookiePair(await PostLoginAsync("ptn.mail"))).GetAsync("/Account/Mfa", Ct)).Content.ReadAsStringAsync(Ct);
        var noMail = await (await _web.NewClient(otherCookie: CookiePair(await PostLoginAsync("ptn.nomail"))).GetAsync("/Account/Mfa", Ct)).Content.ReadAsStringAsync(Ct);

        Assert.Contains("action=\"/Account/MfaEmail\"", withMail, StringComparison.Ordinal);
        Assert.Contains("BTN.MFA_EMAIL_SEND", withMail, StringComparison.Ordinal);
        Assert.DoesNotContain("MfaEmail", noMail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 보내면_번호입력화면_맞는번호로_세션_틀리면_다시입력()
    {
        var ticketCookie = CookiePair(await PostLoginAsync("ptn.mail"));

        var sent = await PostAsync("/Account/MfaEmail", ticketCookie, []);
        var html = await sent.Content.ReadAsStringAsync(Ct);
        var code = _web.Mailer.LastCode;
        var wrongCode = code == "000000" ? "111111" : "000000";
        var wrong = await PostAsync("/Account/Mfa", ticketCookie, [new("Code", wrongCode), new("Email", "true")]);
        var right = await PostAsync("/Account/Mfa", ticketCookie, [new("Code", code), new("Email", "true"), new("ReturnUrl", "/test-screen/page")]);

        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        Assert.Contains("AUTH.OTP_SENT", html, StringComparison.Ordinal);
        Assert.Contains("name=\"Email\" value=\"true\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<svg", html, StringComparison.Ordinal); // 인증 앱 등록 안내는 숨긴다
        Assert.DoesNotContain(code, html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Contains("AUTH.OTP_INVALID", await wrong.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Null(SetCookie(wrong, SessionCookie.Name));
        Assert.Equal("/test-screen/page", right.Headers.Location!.OriginalString);
        Assert.NotNull(SetCookie(right, SessionCookie.Name));
    }

    [Fact]
    public async Task 이메일없는계정이_보내기를눌러도_메일은가지않는다()
    {
        var before = _web.Mailer.Sent.Count;

        var response = await PostAsync("/Account/MfaEmail", CookiePair(await PostLoginAsync("ptn.nomail")), []);

        Assert.Equal(before, _web.Mailer.Sent.Count);
        Assert.Contains("AUTH.OTP_MAIL_FAILED", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 메일서버오류는_화면에_실패안내()
    {
        _web.Mailer.Fail = true;
        try
        {
            var response = await PostAsync("/Account/MfaEmail", CookiePair(await PostLoginAsync("ptn.mail")), []);

            Assert.Contains("AUTH.OTP_MAIL_FAILED", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }
        finally
        {
            _web.Mailer.Fail = false;
        }
    }

    [Fact]
    public async Task 대기표없이_보내기는_로그인화면()
    {
        var response = await PostAsync("/Account/MfaEmail", null, []);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("autocomplete=\"current-password\"", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    private static string CookiePair(HttpResponseMessage response) => SetCookie(response, AccountController.MfaCookie)!.Split(';')[0];

    private async Task<HttpResponseMessage> PostLoginAsync(string userId)
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(), "/Account/Login");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
        {
            Content = new FormUrlEncodedContent([new("__RequestVerificationToken", csrf), new("UserId", userId), new("Password", Password)]),
        };
        return await _web.NewClient(otherCookie: cookie).SendAsync(request, Ct);
    }

    private async Task<HttpResponseMessage> PostAsync(string path, string? ticketCookie, List<KeyValuePair<string, string>> fields)
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(), "/Account/Login");
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent([new("__RequestVerificationToken", csrf), .. fields]),
        };
        return await _web.NewClient(otherCookie: ticketCookie is null ? cookie : $"{cookie}; {ticketCookie}").SendAsync(request, Ct);
    }

    private static AuthUser User(string userId, string hasEmail) => new()
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
        HasEmail = hasEmail,
    };
}
