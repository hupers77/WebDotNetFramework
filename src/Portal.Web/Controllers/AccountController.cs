using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using Portal.Core.Keys;
using Portal.Framework.Web;
using Portal.Framework.Web.Authentication;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Context;
using Portal.Framework.Web.Mvc;
using Portal.Security.Authentication;
using Portal.Web.Models;

namespace Portal.Web.Controllers;

/// <summary>ID/PW 로그인·로그아웃 (설계서 5장). AD SSO는 개발계에서 같은 화면에 붙인다.</summary>
[AllowAnonymous]
public sealed class AccountController(AuthService auth, UserLanguageService languages) : PortalController
{
    // 주소창으로 넘어온 reason은 이 키만 보여 준다 (임의 문구 주입 방지)
    private static readonly string[] ReasonKeys =
        [AuthMessageKeys.SessionExpired, AuthMessageKeys.SessionKicked, AuthMessageKeys.Expired, MessageKeys.LoginRequired];

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

        if (!result.Succeeded || result.SessionToken is null)
        {
            return View(new LoginViewModel(form.UserId, form.ReturnUrl, result.MessageKey ?? MessageKeys.LoginFailed));
        }

        SessionCookie.Write(Response, result.SessionToken);
        return result.PasswordChangeRequired
            ? LocalRedirect(PermissionFilter.PasswordPath)
            : LocalRedirect(SafeReturnUrl(form.ReturnUrl));
    }

    /// <summary>로그아웃. 세션이 이미 끝났어도 쿠키를 지우고 로그인 화면으로 보낸다.</summary>
    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await auth.LogoutAsync(SessionCookie.Read(Request), Request.Headers[HeaderNames.UserAgent].ToString(), HttpContext.RequestAborted);
        SessionCookie.Delete(Response);
        return LocalRedirect(PortalSessionHandler.LoginPath);
    }

    /// <summary>로그인 화면 언어 선택 (쿠키에만 저장).</summary>
    [HttpPost]
    public async Task<IActionResult> Language([FromForm] string lang, [FromForm] string? returnUrl)
    {
        await languages.ChangeAsync(lang, HttpContext.RequestAborted);
        return LocalRedirect(PortalSessionHandler.LoginPath + QueryString.Create("returnUrl", SafeReturnUrl(returnUrl)));
    }

    // 다른 사이트로 보내는 오픈 리디렉트를 막는다
    private string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) && !returnUrl.StartsWith(PortalSessionHandler.LoginPath, StringComparison.OrdinalIgnoreCase)
            ? returnUrl
            : "/";
}
