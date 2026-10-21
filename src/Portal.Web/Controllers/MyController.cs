using Microsoft.AspNetCore.Mvc;
using Portal.Core.Config;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Localization;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Context;
using Portal.Framework.Web.Mvc;
using Portal.Security.Authentication;
using Portal.Security.Passwords;
using Portal.Web.Models;

namespace Portal.Web.Controllers;

/// <summary>내 정보 화면. 이번 단계는 비밀번호 변경(<c>MY_PWD_CHG</c>)만 있다.</summary>
public sealed class MyController(PasswordService passwords, IPortalConfig config, IPortalLocalizer localizer) : PortalController
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

    private PasswordViewModel Model(string? error) => new(
        config.GetInt(ConfigKeys.PwdMinLen3types),
        config.GetInt(ConfigKeys.PwdMinLen2types),
        HttpContext.Features.Get<PortalUserFeature>()?.PasswordChangeRequired == true,
        error);
}
