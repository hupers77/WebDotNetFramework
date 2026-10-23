using Microsoft.AspNetCore.Razor.TagHelpers;
using Portal.Infrastructure.Html;

namespace Portal.Framework.Web.TagHelpers;

/// <summary>
/// 사용자가 입력한 HTML(게시글 내용 등)을 화면에 그린다 (개발 표준 가이드 4장). <c>Html.Raw</c> 대신 이것만 쓴다.
/// 저장할 때 이미 거르지만, 그리기 직전에 한 번 더 <see cref="HtmlSanitizer"/>를 거쳐 허용한 태그만 나가게 한다.
/// </summary>
/// <example><c>&lt;fw-safe-html value="@Model.Content" class="board-content" /&gt;</c></example>
[HtmlTargetElement("fw-safe-html")]
public sealed class FwSafeHtmlTagHelper : TagHelper
{
    /// <summary>그릴 HTML.</summary>
    [HtmlAttributeName("value")]
    public string? Value { get; set; }

    /// <inheritdoc />
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Content.SetHtmlContent(HtmlSanitizer.Sanitize(Value));
    }
}
