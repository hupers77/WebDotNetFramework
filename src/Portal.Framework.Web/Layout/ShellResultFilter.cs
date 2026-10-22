using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Portal.Core.Config;
using Portal.Core.Keys;
using Portal.Framework.Web.Context;
using Portal.Framework.Web.Mvc;

namespace Portal.Framework.Web.Layout;

/// <summary>
/// 멀티 탭 레이아웃 (설계서 12장, 설정 <c>UI.TAB_MODE</c>). 주소창으로 화면을 직접 열면 화면 대신 셸(헤더·메뉴·탭)을 보내고,
/// 셸이 그 화면을 첫 탭(iframe, <c>_layout=tab</c>)으로 연다. 그래서 새로고침·즐겨찾기·링크 공유가 모두 그대로 동작한다.
/// 탭 안 요청, AJAX, 비밀번호 변경 강제 중, 오류 화면 재실행에는 끼어들지 않는다.
/// </summary>
public sealed class ShellResultFilter(IPortalConfig config) : IAsyncResultFilter
{
    /// <summary>셸 View 이름 (호스트의 <c>Views/Shared/_Shell.cshtml</c>).</summary>
    public const string ShellView = "_Shell";

    /// <inheritdoc />
    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (context.Result is ViewResult view && ShouldWrap(http))
        {
            var request = http.Request;
            var model = new ShellModel(request.PathBase + request.Path + request.QueryString, Math.Max(1, config.GetInt(ConfigKeys.UiMaxTabs)));
            context.Result = new ViewResult
            {
                ViewName = ShellView,
                ViewData = new ViewDataDictionary<ShellModel>(view.ViewData, model),
                TempData = view.TempData,
                StatusCode = view.StatusCode,
            };
        }

        return next();
    }

    private bool ShouldWrap(HttpContext http) =>
        HttpMethods.IsGet(http.Request.Method)
        && !RequestKind.WantsJson(http.Request)
        && !PortalLayouts.IsTabFrame(http.Request)
        && http.Features.Get<PortalUserFeature>() is { PasswordChangeRequired: false }
        && http.Features.Get<IStatusCodeReExecuteFeature>() is null
        && http.Features.Get<IExceptionHandlerFeature>() is null
        && config.GetBool(ConfigKeys.UiTabMode);
}

/// <summary>셸 화면 정보.</summary>
/// <param name="InitialUrl">첫 탭으로 열 주소.</param>
/// <param name="MaxTabs">최대 탭 수 (<c>UI.MAX_TABS</c>). 넘으면 가장 오래 안 쓴 탭을 닫는다.</param>
public sealed record ShellModel(string InitialUrl, int MaxTabs);

/// <summary>화면 레이아웃 판별.</summary>
public static class PortalLayouts
{
    /// <summary>탭 안 화면 요청을 표시하는 쿼리 (<c>_layout=tab</c>).</summary>
    public const string LayoutQuery = "_layout";

    /// <summary>탭(iframe) 안에서 열린 화면인지. 셸이 붙인 쿼리 또는 브라우저의 <c>Sec-Fetch-Dest: iframe</c>(탭 안 링크 이동).</summary>
    public static bool IsTabFrame(HttpRequest request) =>
        request.Query[LayoutQuery] == "tab"
        || string.Equals(request.Headers["Sec-Fetch-Dest"], "iframe", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 화면 레이아웃 이름 (<c>_ViewStart</c>가 쓴다). 로그인 전(로그인·오류 화면)은 메뉴 없는 <c>_AuthLayout</c>,
    /// 탭(iframe) 안은 본문만 있는 <c>_TabLayout</c>, 그 밖은 헤더·메뉴가 있는 <c>_PortalLayout</c>.
    /// </summary>
    public static string For(HttpContext context) =>
        context.Features.Get<Context.PortalUserFeature>() is null ? "_AuthLayout"
        : IsTabFrame(context.Request) ? "_TabLayout"
        : "_PortalLayout";
}
