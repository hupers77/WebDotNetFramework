using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Portal.Framework.Web.TagHelpers;

/// <summary>
/// 아이콘 모음(<c>wwwroot/img/icons.svg</c>)의 아이콘 하나. 글자 옆 장식이므로 화면 낭독기는 읽지 않는다(<c>aria-hidden</c>).
/// 크기·색은 CSS <c>.ic</c>가 글자 크기와 색을 따른다. 아이콘만 있는 버튼은 버튼에 <c>aria-label</c>을 붙인다.
/// JS에서는 <c>fw.icon('bell')</c>로 같은 아이콘을 만든다.
/// </summary>
/// <example><c>&lt;fw-icon name="bell" /&gt;</c></example>
[HtmlTargetElement("fw-icon", TagStructure = TagStructure.WithoutEndTag)]
public sealed partial class FwIconTagHelper(IFileVersionProvider versions) : TagHelper
{
    /// <summary>아이콘 모음 경로.</summary>
    public const string SpritePath = "/img/icons.svg";

    /// <summary>아이콘 이름 (예: <c>bell</c>, 메뉴는 공통코드 ICON 값).</summary>
    [HtmlAttributeName("name")]
    public string Name { get; set; } = "";

    /// <summary>현재 화면 정보 (Razor가 채운다).</summary>
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    /// <summary>캐시를 깨는 버전이 붙은 아이콘 모음 주소.</summary>
    public static string SpriteUrl(IFileVersionProvider versions, Microsoft.AspNetCore.Http.HttpContext context) =>
        versions.AddFileVersionToPath(context.Request.PathBase, context.Request.PathBase + SpritePath);

    /// <inheritdoc />
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (!NamePattern().IsMatch(Name))
        {
            output.SuppressOutput();
            return;
        }

        output.TagName = "svg";
        output.TagMode = TagMode.StartTagAndEndTag;
        var css = output.Attributes.TryGetAttribute("class", out var existing) ? $"ic {existing.Value}" : "ic";
        output.Attributes.SetAttribute("class", css);
        output.Attributes.SetAttribute("aria-hidden", "true");
        output.Attributes.SetAttribute("focusable", "false");
        output.Content.SetHtmlContent($"<use href=\"{SpriteUrl(versions, ViewContext.HttpContext)}#i-{Name}\"></use>");
    }

    [GeneratedRegex("^[a-z0-9-]{1,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
