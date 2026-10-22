using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using Portal.Core.Config;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Localization;
using Portal.Framework.Web;
using Portal.Framework.Web.Authentication;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Context;
using Portal.Framework.Web.Mvc;
using Portal.Security.Authentication;
using Portal.Security.Mfa;
using Portal.Security.Passwords;
using Portal.Web.Infrastructure;
using Portal.Web.Models;

namespace Portal.Web.Controllers;

/// <summary>
/// ID/PW 로그인·로그아웃, 2단계 인증(TOTP), 협력사 가입 신청 (설계서 5장). AD SSO는 3단계에서 같은 화면에 붙인다.
/// 2단계 인증 대상은 비밀번호를 확인하면 대기표 쿠키를 받고 <see cref="Mfa(string?)"/> 화면에서 코드를 입력한다.
/// </summary>
[AllowAnonymous]
public sealed class AccountController(
    AuthService auth,
    UserLanguageService languages,
    PartnerJoinService joins,
    MfaTicketProtector tickets,
    IPortalConfig config,
    IPortalLocalizer localizer) : PortalController
{
    // 주소창으로 넘어온 reason은 이 키만 보여 준다 (임의 문구 주입 방지)
    private static readonly string[] ReasonKeys =
        [AuthMessageKeys.SessionExpired, AuthMessageKeys.SessionKicked, AuthMessageKeys.Expired, MessageKeys.LoginRequired, AuthMessageKeys.MfaExpired];

    /// <summary>2단계 인증 대기표 쿠키. 코드 입력 화면에만 쓰므로 SameSite=Strict.</summary>
    internal const string MfaCookie = "__Host-portal-mfa";

    /// <summary>로그인 화면.</summary>
    [HttpGet]
    public IActionResult Login(string? returnUrl, string? reason)
    {
        if (HttpContext.Features.Get<PortalUserFeature>() is not null)
        {
            return LocalRedirect(SafeReturnUrl(returnUrl));
        }

        return View(new LoginViewModel(null, returnUrl, ReasonKeys.Contains(reason) ? reason : null));
    }

    /// <summary>로그인 처리. 성공하면 새 세션 쿠키를 쓰고, 비밀번호 변경이 필요하면 변경 화면으로 보낸다.</summary>
    [HttpPost, EnableRateLimiting(PortalWebExtensions.LoginRateLimitPolicy)]
    public async Task<IActionResult> Login([FromForm] LoginForm form)
    {
        var result = string.IsNullOrWhiteSpace(form.UserId) || string.IsNullOrEmpty(form.Password)
            ? new LoginResult(false, MessageKeys.LoginFailed)
            : await auth.LoginAsync(
                new LoginRequest(form.UserId.Trim(), form.Password, Request.Headers[HeaderNames.UserAgent].ToString()),
                HttpContext.RequestAborted);

        if (result is { MfaRequired: true, UserId: { } userId })
        {
            // 처음 등록하는 계정은 새 비밀키를 대기표에만 담는다 (코드를 확인해야 DB에 저장)
            WriteTicket(new MfaTicket(userId, result.MfaEnrolled ? null : Totp.NewSecret()));
            return RedirectToAction(nameof(Mfa), new { returnUrl = SafeReturnUrl(form.ReturnUrl) });
        }

        return SignedIn(result, form.UserId, form.ReturnUrl);
    }

    /// <summary>2단계 인증 화면. 처음이면 인증 앱 등록 안내(QR·등록 키)를 함께 보여 준다.</summary>
    [HttpGet]
    public IActionResult Mfa(string? returnUrl)
    {
        var ticket = tickets.Unprotect(Request.Cookies[MfaCookie]);
        return ticket is null
            ? LocalRedirect(PortalSessionHandler.LoginPath + QueryString.Create("reason", AuthMessageKeys.MfaExpired))
            : View(MfaModel(ticket, returnUrl, null));
    }

    /// <summary>
    /// 2단계 인증 코드 확인. 맞으면 세션을 만들고, 틀리면 다시 입력받는다(틀린 횟수는 비밀번호 실패와 함께 세어 잠근다).
    /// 대기표가 만료되었거나 계정을 쓸 수 없게 되었으면 로그인 화면으로 돌아간다.
    /// </summary>
    [HttpPost, ActionName(nameof(Mfa)), EnableRateLimiting(PortalWebExtensions.LoginRateLimitPolicy)]
    public async Task<IActionResult> MfaVerify([FromForm] MfaForm form)
    {
        var ticket = tickets.Unprotect(Request.Cookies[MfaCookie]);
        if (ticket is null)
        {
            return View(nameof(Login), new LoginViewModel(null, form.ReturnUrl, AuthMessageKeys.MfaExpired));
        }

        var result = await auth.VerifyMfaAsync(
            new MfaVerifyRequest(ticket, form.Code, Request.Headers[HeaderNames.UserAgent].ToString()),
            HttpContext.RequestAborted);
        if (!result.Succeeded && result.MessageKey == AuthMessageKeys.MfaInvalid)
        {
            return View(nameof(Mfa), MfaModel(ticket, form.ReturnUrl, result.MessageKey));
        }

        Response.Cookies.Delete(MfaCookie, TicketCookieOptions());
        return SignedIn(result, ticket.UserId, form.ReturnUrl);
    }

    /// <summary>로그아웃. 세션이 이미 끝났어도 쿠키를 지우고 로그인 화면으로 보낸다.</summary>
    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await auth.LogoutAsync(SessionCookie.Read(Request), Request.Headers[HeaderNames.UserAgent].ToString(), HttpContext.RequestAborted);
        SessionCookie.Delete(Response);
        return LocalRedirect(PortalSessionHandler.LoginPath);
    }

    /// <summary>협력사 가입 신청 화면.</summary>
    [HttpGet]
    public IActionResult Join() => View(JoinModel(new JoinForm(), null));

    /// <summary>
    /// 협력사 가입 신청. 본사 담당자가 승인하면 같은 ID·비밀번호로 로그인할 수 있다.
    /// 로그인과 같은 요청 한도를 둔다(ID 존재 여부를 대량으로 확인하지 못하게).
    /// </summary>
    [HttpPost, EnableRateLimiting(PortalWebExtensions.LoginRateLimitPolicy)]
    public async Task<IActionResult> Join([FromForm] JoinForm form)
    {
        var shown = new JoinForm { BizRegNo = form.BizRegNo, UserId = form.UserId, UserNm = form.UserNm, Email = form.Email };
        if (string.IsNullOrWhiteSpace(form.BizRegNo) || string.IsNullOrWhiteSpace(form.UserId) || string.IsNullOrWhiteSpace(form.UserNm)
            || string.IsNullOrWhiteSpace(form.Email) || string.IsNullOrEmpty(form.Password))
        {
            return View(JoinModel(shown, localizer[MessageKeys.InvalidInput]));
        }

        if (form.Password != form.PasswordConfirm)
        {
            return View(JoinModel(shown, localizer[PasswordMessageKeys.ConfirmMismatch]));
        }

        try
        {
            await joins.RequestAsync(new PartnerJoinRequest(form.BizRegNo, form.UserId, form.UserNm, form.Email, form.Password), HttpContext.RequestAborted);
        }
        catch (BizException ex)
        {
            return View(JoinModel(shown, localizer[ex.MessageKey, [.. ex.Args]]));
        }

        return View(JoinModel(new JoinForm(), null) with { Done = true });
    }

    /// <summary>로그인 화면 언어 선택 (쿠키에만 저장).</summary>
    [HttpPost]
    public async Task<IActionResult> Language([FromForm] string lang, [FromForm] string? returnUrl)
    {
        await languages.ChangeAsync(lang, HttpContext.RequestAborted);
        return LocalRedirect(PortalSessionHandler.LoginPath + QueryString.Create("returnUrl", SafeReturnUrl(returnUrl)));
    }

    // 로그인 결과: 성공이면 세션 쿠키를 쓰고 이동, 실패면 로그인 화면에 메시지
    private IActionResult SignedIn(LoginResult result, string? userId, string? returnUrl)
    {
        if (!result.Succeeded || result.SessionToken is null)
        {
            return View(nameof(Login), new LoginViewModel(userId, returnUrl, result.MessageKey ?? MessageKeys.LoginFailed));
        }

        SessionCookie.Write(Response, result.SessionToken);
        return result.PasswordChangeRequired
            ? LocalRedirect(PermissionFilter.PasswordPath)
            : LocalRedirect(SafeReturnUrl(returnUrl));
    }

    private MfaViewModel MfaModel(MfaTicket ticket, string? returnUrl, string? messageKey)
    {
        MfaEnrollInfo? enroll = null;
        if (ticket.PendingSecret is { } secret)
        {
            var issuer = config.GetString(ConfigKeys.SiteName) is { Length: > 0 } name ? name : "Portal";
            var uri = Totp.KeyUri(issuer, ticket.UserId, secret);
            var key = string.Join(' ', Totp.ToBase32(secret).Chunk(4).Select(c => new string(c)));
            enroll = new MfaEnrollInfo(key, uri, QrSvg.Render(uri, localizer["MFA.QR_LABEL"]));
        }

        return new MfaViewModel(SafeReturnUrl(returnUrl), messageKey, enroll);
    }

    private void WriteTicket(MfaTicket ticket)
    {
        var options = TicketCookieOptions();
        options.MaxAge = MfaTicketProtector.Lifetime;
        Response.Cookies.Append(MfaCookie, tickets.Protect(ticket), options);
    }

    private static CookieOptions TicketCookieOptions() => new()
    {
        Secure = true,
        HttpOnly = true,
        SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict,
        Path = "/",
        IsEssential = true,
    };

    private JoinViewModel JoinModel(JoinForm form, string? error) =>
        new(form, config.GetInt(ConfigKeys.PwdMinLen3types), config.GetInt(ConfigKeys.PwdMinLen2types), error);

    // 다른 사이트로 보내는 오픈 리디렉트를 막는다
    private string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) && !returnUrl.StartsWith(PortalSessionHandler.LoginPath, StringComparison.OrdinalIgnoreCase)
            ? returnUrl
            : "/";
}
