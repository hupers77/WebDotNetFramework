using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Portal.Framework.Web.Mvc;

/// <summary>요청 종류 판단: 화면(HTML)인지 AJAX·API(JSON)인지에 따라 오류 응답을 다르게 한다.</summary>
public static class RequestKind
{
    /// <summary><c>fw.ajax</c>가 보내는 요청 헤더.</summary>
    public const string AjaxHeader = "X-Requested-With";

    /// <summary>
    /// 사용자가 하지 않은 백그라운드 요청 표시 헤더 (헤더 알림 배지 조회). 이 요청은 세션 비활동 시각을 늘리지 않는다.
    /// 값을 꾸며 보내도 자기 세션이 늘지 않을 뿐이라 보안 영향이 없다.
    /// </summary>
    public const string BackgroundHeader = "X-Portal-Background";

    /// <summary>백그라운드 요청인지 (<see cref="BackgroundHeader"/>가 <c>1</c>).</summary>
    public static bool IsBackground(HttpRequest request) => request.Headers[BackgroundHeader] == "1";

    /// <summary>JSON 응답을 기대하는 요청: <c>/api/</c> 경로, <c>fw.ajax</c> 요청, Accept가 JSON.</summary>
    public static bool WantsJson(HttpRequest request) =>
        request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        || request.Headers[AjaxHeader] == "XMLHttpRequest"
        || request.Headers[HeaderNames.Accept].ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
}
