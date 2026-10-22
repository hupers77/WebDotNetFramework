using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Core.Config;
using Portal.Core.Keys;
using Portal.Framework.Web.Mvc;
using Portal.Infrastructure.Localization;

namespace Portal.Web.Controllers;

/// <summary>
/// 화면이 쓰는 동적 자원: 언어별 문구 묶음(<c>fw.t</c>)과 회사 브랜드 색상 CSS(CSP가 인라인 스타일을 막으므로 파일로 준다).
/// 로그인 전 화면도 쓰므로 익명 허용이고, 문구는 공개해도 되는 화면 문구뿐이다.
/// </summary>
[AllowAnonymous]
public sealed partial class AssetsController(LangResources resources, IPortalConfig config) : PortalController
{
    private const string Immutable = "public, max-age=31536000, immutable";

    /// <summary>언어별 문구 묶음. <c>v</c>가 현재 버전이면 1년 캐시, 아니면 매번 확인.</summary>
    [HttpGet("i18n/{lang}.json")]
    public IActionResult Texts(string lang, string? v)
    {
        if (!resources.Languages.Any(l => l.LangCd == lang))
        {
            return NotFound();
        }

        Response.Headers.CacheControl = v == resources.Version ? Immutable : "no-cache";
        return Json(resources.GetAll(lang));
    }

    /// <summary>브랜드 색상 (<c>SITE.THEME_COLOR</c>). 형식이 잘못된 값은 기본 색을 쓴다.</summary>
    [HttpGet("theme.css")]
    public ContentResult Theme()
    {
        var color = config.GetString(ConfigKeys.SiteThemeColor);
        var css = color is not null && HexColor().IsMatch(color) ? $":root {{ --fw-brand: {color}; }}\n" : "";
        Response.Headers.CacheControl = "no-cache";
        return Content(css, "text/css");
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColor();
}
