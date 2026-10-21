using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Portal.Core.Localization;
using Portal.Core.Results;

namespace Portal.Framework.Web.Mvc;

/// <summary>추적 ID를 채운 <see cref="ApiResult"/> 응답을 만든다.</summary>
public static class ApiResults
{
    /// <summary>현재 요청의 추적 ID (W3C traceparent 형식, 없으면 요청 ID).</summary>
    public static string TraceId(HttpContext context) => Activity.Current?.Id ?? context.TraceIdentifier;

    /// <summary>실패 응답. 메시지는 사용자 언어로 번역한다.</summary>
    public static ApiResult Fail(HttpContext context, string messageKey, params object?[] args)
    {
        var localizer = context.RequestServices.GetService<IPortalLocalizer>();
        var result = ApiResult.Fail(messageKey, localizer?[messageKey, args] ?? messageKey);
        result.TraceId = TraceId(context);
        return result;
    }
}
