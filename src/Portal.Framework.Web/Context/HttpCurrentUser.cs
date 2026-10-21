using Microsoft.AspNetCore.Http;
using Portal.Core.Context;
using Portal.Infrastructure.Localization;

namespace Portal.Framework.Web.Context;

/// <summary>
/// 웹 요청의 <see cref="ICurrentUser"/>. 로그인 사용자는 세션 정보를, 로그인 전에는 언어 쿠키 → 브라우저 언어 → 기본 언어를 쓴다.
/// 요청 밖(배경 작업)에서는 비로그인 사용자로 동작한다.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    /// <summary>로그인 전 언어 선택 쿠키.</summary>
    public const string LangCookie = "portal-lang";

    /// <summary>지원 언어 (초기 데이터 <c>TB_LANG</c>).</summary>
    internal static readonly string[] SupportedLangs = ["ko", "en", "zh-CN"];

    private PortalUserFeature? Feature => accessor.HttpContext?.Features.Get<PortalUserFeature>();

    /// <inheritdoc />
    public bool IsAuthenticated => Feature is not null;

    /// <inheritdoc />
    public string? UserId => Feature?.User.UserId;

    /// <inheritdoc />
    public string? CompanyCd => Feature?.User.CompanyCd;

    /// <inheritdoc />
    public string? UserTypeCd => Feature?.User.UserTypeCd;

    /// <inheritdoc />
    public string LangCd => Feature?.User.LangCd ?? AnonymousLang(accessor.HttpContext);

    /// <inheritdoc />
    public string? ClientIp => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    internal static string AnonymousLang(HttpContext? context)
    {
        if (context is null)
        {
            return LangResources.DefaultLang;
        }

        if (context.Request.Cookies.TryGetValue(LangCookie, out var cookie) && Match(cookie) is { } chosen)
        {
            return chosen;
        }

        var accept = context.Request.GetTypedHeaders().AcceptLanguage;
        foreach (var lang in accept.OrderByDescending(a => a.Quality ?? 1))
        {
            if (Match(lang.Value.Value) is { } match)
            {
                return match;
            }
        }

        return LangResources.DefaultLang;
    }

    // zh, zh-Hans, zh-CN → zh-CN / en-US → en / ko-KR → ko
    private static string? Match(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var exact = Array.Find(SupportedLangs, l => l.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var primary = value.Split('-')[0];
        return primary.ToUpperInvariant() switch
        {
            "KO" => "ko",
            "EN" => "en",
            "ZH" => "zh-CN",
            _ => null,
        };
    }
}
