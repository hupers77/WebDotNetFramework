using Microsoft.AspNetCore.Http;
using Portal.Core.Context;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Data;
using Portal.Infrastructure.Localization;

namespace Portal.Framework.Web.Context;

/// <summary>화면 언어 전환 (헤더·로그인 화면). 로그인 사용자는 계정(<c>TB_USER.LANG_CD</c>)에, 로그인 전에는 쿠키에 저장한다.</summary>
public sealed class UserLanguageService(ISpExecutor sp, ICurrentUser currentUser, LangResources resources, IHttpContextAccessor accessor)
{
    internal const string SaveSp = "FW.USP_USER_LANG_SAVE";

    /// <summary>언어를 바꾼다. 다음 로그인 화면에서도 같은 언어가 나오도록 쿠키도 쓴다.</summary>
    /// <exception cref="BizException">사용하지 않는 언어.</exception>
    public async Task ChangeAsync(string langCd, CancellationToken cancellationToken = default)
    {
        if (!resources.Languages.Any(l => l.LangCd == langCd))
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        if (currentUser.IsAuthenticated)
        {
            await sp.ExecuteAsync(SaveSp, new { P_NEW_LANG_CD = langCd }, cancellationToken);
        }

        accessor.HttpContext?.Response.Cookies.Append(HttpCurrentUser.LangCookie, langCd, new CookieOptions
        {
            Secure = true,
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = TimeSpan.FromDays(365),
            IsEssential = true,
        });
    }
}
