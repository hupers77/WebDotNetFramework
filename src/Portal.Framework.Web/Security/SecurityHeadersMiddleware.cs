using Microsoft.AspNetCore.Http;

namespace Portal.Framework.Web.Security;

/// <summary>
/// 보안 응답 헤더 (설계서 9장). 인라인 스크립트·이벤트 속성은 CSP로 막으므로 화면 JS는 파일로만 둔다(개발 표준 가이드 5장).
/// 값은 보안상 낮추면 안 되는 값이라 설정으로 열지 않는다. HSTS는 호스트가 운영 환경에서 <c>UseHsts</c>로 붙인다.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    internal const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; " +
        "connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'self'";

    /// <summary>헤더를 붙이고 다음 단계로 넘긴다.</summary>
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var response = (HttpResponse)state;
            var headers = response.Headers;
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "SAMEORIGIN";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";

            // 정적 파일 외에는 브라우저·프록시가 저장하지 않게 한다 (로그아웃 후 뒤로 가기로 화면이 보이지 않도록)
            if (!headers.ContainsKey("Cache-Control"))
            {
                headers.CacheControl = "no-store";
            }

            return Task.CompletedTask;
        }, context.Response);

        return next(context);
    }
}
