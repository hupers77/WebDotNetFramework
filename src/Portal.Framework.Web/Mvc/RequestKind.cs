using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Portal.Framework.Web.Mvc;

/// <summary>요청 종류 판단: 화면(HTML)인지 AJAX·API(JSON)인지에 따라 오류 응답을 다르게 한다.</summary>
public static class RequestKind
{
    /// <summary><c>fw.ajax</c>가 보내는 요청 헤더.</summary>
    public const string AjaxHeader = "X-Requested-With";

    /// <summary>JSON 응답을 기대하는 요청: <c>/api/</c> 경로, <c>fw.ajax</c> 요청, Accept가 JSON.</summary>
    public static bool WantsJson(HttpRequest request) =>
        request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        || request.Headers[AjaxHeader] == "XMLHttpRequest"
        || request.Headers[HeaderNames.Accept].ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
}
