using Microsoft.AspNetCore.Http;

namespace Portal.Framework.Web.Api;

/// <summary>토큰을 확인한 API 호출자. Handler는 <see cref="ApiCallerExtensions.GetApiCaller"/>로 얻는다.</summary>
/// <param name="ClientId">API 클라이언트 ID.</param>
/// <param name="ApiCodes">이 호출자에게 허용된 API 코드.</param>
public sealed record ApiCaller(string ClientId, IReadOnlySet<string> ApiCodes);

/// <summary><see cref="ApiCaller"/> 접근.</summary>
public static class ApiCallerExtensions
{
    internal const string ItemKey = "Portal.ApiCaller";

    /// <summary>현재 요청의 API 호출자. 토큰이 필요 없는 API에서는 null.</summary>
    public static ApiCaller? GetApiCaller(this HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) ? value as ApiCaller : null;
}
