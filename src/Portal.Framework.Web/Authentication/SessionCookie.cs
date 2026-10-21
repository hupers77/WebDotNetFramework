using Microsoft.AspNetCore.Http;

namespace Portal.Framework.Web.Authentication;

/// <summary>
/// 세션 쿠키 (설계서 5장 '인증 쿠키'). <c>__Host-</c> 접두어 + Secure + HttpOnly + SameSite=Lax + Path=/ 는
/// 보안상 낮추면 안 되는 값이라 설정으로 열지 않는다. 만료 시각을 두지 않아 브라우저를 닫으면 사라진다.
/// </summary>
public static class SessionCookie
{
    /// <summary>쿠키 이름.</summary>
    public const string Name = "__Host-portal-session";

    /// <summary>요청의 세션 토큰.</summary>
    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) ? value : null;

    /// <summary>새 세션 토큰을 쓴다 (로그인 성공 시 항상 새 값: 세션 고정 방지).</summary>
    public static void Write(HttpResponse response, string token) =>
        response.Cookies.Append(Name, token, Options());

    /// <summary>쿠키를 지운다.</summary>
    public static void Delete(HttpResponse response) =>
        response.Cookies.Delete(Name, Options());

    private static CookieOptions Options() => new()
    {
        Secure = true,
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        IsEssential = true,
    };
}
