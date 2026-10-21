using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Portal.Core.Keys;
using Portal.Framework.Web.Mvc;
using Portal.Web.Models;

namespace Portal.Web.Controllers;

/// <summary>
/// 공통 오류 화면 (설계서 9장 '정보 노출'). 예외 내용·스택은 보여 주지 않고 추적 ID만 보여 준다.
/// 원래 요청이 POST여도 다시 실행되므로 모든 메서드를 받고 CSRF 검사를 하지 않는다(상태를 바꾸지 않음).
/// </summary>
[AllowAnonymous, IgnoreAntiforgeryToken]
[Route("Error")]
public sealed class ErrorController : PortalController
{
    /// <summary>처리되지 않은 예외.</summary>
    [Route("")]
    public IActionResult Index() => Show(StatusCodes.Status500InternalServerError);

    /// <summary>본문 없이 끝난 4xx/5xx 응답.</summary>
    [Route("{code:int}")]
    public IActionResult Status(int code) => Show(code);

    private ViewResult Show(int code)
    {
        // 다시 실행된 요청이 아니라 주소로 직접 연 경우에도 원래 상태 코드를 흉내 내지 않고 그대로 둔다
        if (HttpContext.Features.Get<IStatusCodeReExecuteFeature>() is not null
            || HttpContext.Features.Get<IExceptionHandlerFeature>() is not null)
        {
            Response.StatusCode = code;
        }

        var (title, message) = code switch
        {
            StatusCodes.Status403Forbidden => ("LBL.FORBIDDEN_TITLE", MessageKeys.Forbidden),
            StatusCodes.Status404NotFound => ("LBL.ERROR_TITLE", MessageKeys.NotFound),
            StatusCodes.Status429TooManyRequests => ("LBL.ERROR_TITLE", MessageKeys.TooManyRequests),
            _ => ("LBL.ERROR_TITLE", MessageKeys.SystemError),
        };
        return View("Error", new ErrorViewModel(code, title, message, ApiResults.TraceId(HttpContext)));
    }
}
