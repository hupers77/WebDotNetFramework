using Microsoft.AspNetCore.Mvc;
using Portal.Core.Config;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Localization;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Context;
using Portal.Framework.Web.Mvc;
using Portal.Framework.Web.Navigation;
using Portal.Security.Authentication;
using Portal.Security.Passwords;
using Portal.Web.Models;

namespace Portal.Web.Controllers;

/// <summary>내 정보 화면. 이번 단계는 비밀번호 변경(<c>MY_PWD_CHG</c>)만 있다.</summary>
public sealed class MyController(PasswordService passwords, IPortalConfig config, IPortalLocalizer localizer, UserLanguageService languages, MenuService menus) : PortalController
{
    /// <summary>비밀번호 변경 화면.</summary>
    [HttpGet, Program(PermissionFilter.PasswordProgram)]
    public IActionResult Password() => View(Model(null));

    /// <summary>비밀번호 변경. 성공하면 변경 강제가 풀리므로 홈으로 보낸다.</summary>
    [HttpPost, Program(PermissionFilter.PasswordProgram), Func(FuncCodes.Update)]
    public async Task<IActionResult> Password([FromForm] PasswordForm form)
    {
        if (string.IsNullOrEmpty(form.CurrentPassword) || string.IsNullOrEmpty(form.NewPassword))
        {
            return View(Model(localizer[MessageKeys.InvalidInput]));
        }

        if (form.NewPassword != form.NewPasswordConfirm)
        {
            return View(Model(localizer[PasswordMessageKeys.ConfirmMismatch]));
        }

        try
        {
            await passwords.ChangeAsync(form.CurrentPassword, form.NewPassword, HttpContext.RequestAborted);
        }
        catch (BizException ex)
        {
            return View(Model(localizer[ex.MessageKey, [.. ex.Args]]));
        }

        TempData[TempDataKeys.Notice] = AuthMessageKeys.PasswordChanged;
        return LocalRedirect("/");
    }

    /// <summary>화면 언어 변경 (헤더). 바꾼 뒤 보던 화면으로 돌아간다.</summary>
    [HttpPost, AuthenticatedOnly]
    public async Task<IActionResult> Language([FromForm] string lang, [FromForm] string? returnUrl)
    {
        await languages.ChangeAsync(lang, HttpContext.RequestAborted);
        return LocalRedirect(returnUrl is not null && Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }

    /// <summary>세션 연장 (헤더의 연장 버튼). 세션 확인이 비활동 시각을 늘리므로 본문은 없다.</summary>
    [HttpGet, AuthenticatedOnly]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "MVC Action은 인스턴스 메서드여야 한다")]
    public ApiResult Ping() => Ok();

    /// <summary>즐겨찾기 추가·해제 (메뉴 트리의 ☆ 버튼, AJAX).</summary>
    [HttpPost, AuthenticatedOnly]
    public async Task<ApiResult> Favorite([FromBody] FavoriteRequest request)
    {
        await menus.SetFavoriteAsync(request.MenuId, request.Favorite, HttpContext.RequestAborted);
        return Ok();
    }

    private PasswordViewModel Model(string? error) => new(
        config.GetInt(ConfigKeys.PwdMinLen3types),
        config.GetInt(ConfigKeys.PwdMinLen2types),
        HttpContext.Features.Get<PortalUserFeature>()?.PasswordChangeRequired == true,
        error);
}
