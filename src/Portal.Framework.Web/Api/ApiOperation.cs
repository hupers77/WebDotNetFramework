using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Portal.Framework.Web.Api;

/// <summary>
/// 외부 API 한 개의 정의 (엔드포인트 메타데이터). <see cref="Code"/>가 클라이언트에 허용하는 단위이고(<c>TB_API_CLIENT.API_CODES</c>),
/// OpenAPI 문서(<c>/api/v1/openapi.json</c>)도 이 정보로 만든다.
/// </summary>
/// <param name="Code">API 코드 (영문 대문자·숫자·밑줄, 예: <c>ORDER_GET</c>).</param>
/// <param name="Method">HTTP 메서드.</param>
/// <param name="Summary">설명 (OpenAPI 문서에 실린다).</param>
/// <param name="Anonymous">토큰 없이 부를 수 있는 API (토큰 발급, 문서). 업무 API는 false.</param>
public sealed record ApiOperation(string Code, string Method, string Summary, bool Anonymous = false);

/// <summary>외부 API 엔드포인트 등록.</summary>
public static class ApiEndpointExtensions
{
    /// <summary>외부 API 경로 접두사.</summary>
    public const string BasePath = "/api/v1";

    /// <summary>토큰 발급 API 코드 (기록에서 본문을 남기지 않는 기준).</summary>
    public const string TokenCode = "TOKEN";

    /// <summary>
    /// 외부 API를 등록한다. 주소는 <c>/api/v1</c> 아래이고, 호출자는 Bearer 토큰과 이 <paramref name="code"/>의 허용이 있어야 한다.
    /// Controller 필터(CSRF, 화면 권한)는 적용되지 않으므로 Handler에서 입력을 직접 검증한다.
    /// </summary>
    public static RouteHandlerBuilder MapApi(this IEndpointRouteBuilder app, string method, string pattern, string code, string summary, Delegate handler, bool anonymous = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        var path = BasePath + (pattern.StartsWith('/') ? pattern : "/" + pattern);
        return app.MapMethods(path, [method], handler)
            .WithMetadata(new ApiOperation(code, method.ToUpperInvariant(), summary, anonymous))
            .DisableAntiforgery();
    }
}
