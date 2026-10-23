using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Portal.Core.Keys;
using Portal.Core.Localization;
using Portal.Framework.Web.Authorization;

namespace Portal.Framework.Web.TagHelpers;

/// <summary>
/// 권한이 있을 때만 그려지는 버튼 (개발 표준 가이드 5장). 버튼 표시 여부를 JS에서 판단하지 않는다.
/// 프로그램은 현재 Controller의 <c>[Program]</c>, 문구는 <c>text-key</c>의 다국어 문구다. 나머지 속성(id, class 등)은 그대로 붙는다.
/// </summary>
/// <example><c>&lt;fw-button func="UPDATE" text-key="BTN.SAVE" id="save" class="primary" /&gt;</c></example>
/// <remarks>버튼을 숨기는 것은 화면 편의일 뿐이며, 서버는 Action의 <c>[Func]</c>로 다시 확인한다.</remarks>
[HtmlTargetElement("fw-button")]
public sealed class FwButtonTagHelper(CurrentPermissions permissions, IPortalLocalizer localizer) : TagHelper
{
    /// <summary>필요한 기능 코드 (기본 READ).</summary>
    [HtmlAttributeName("func")]
    public string Func { get; set; } = FuncCodes.Read;

    /// <summary>버튼 문구 키 (예: <c>BTN.SAVE</c>).</summary>
    [HtmlAttributeName("text-key")]
    public string TextKey { get; set; } = "";

    /// <summary>다른 프로그램의 권한으로 판단할 때만 지정한다.</summary>
    [HtmlAttributeName("program")]
    public string? Program { get; set; }

    /// <summary>현재 화면 정보 (Razor가 채운다).</summary>
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var programId = Program ?? PermissionFilter.Resolve(ViewContext.ActionDescriptor.EndpointMetadata).ProgramId;
        var granted = await permissions.GetAsync(ViewContext.HttpContext.RequestAborted);
        if (programId is null || !granted.Can(programId, Func))
        {
            output.SuppressOutput();
            return;
        }

        output.TagName = "button";
        output.TagMode = TagMode.StartTagAndEndTag;
        if (!output.Attributes.ContainsName("type"))
        {
            output.Attributes.SetAttribute("type", "button");
        }

        output.Attributes.SetAttribute("data-func", Func);
        if (Func == FuncCodes.Print)
        {
            // 화면 출력(fw.js)이 이 프로그램으로 PRINT 권한을 확인받는다
            output.Attributes.SetAttribute("data-program", programId);
        }

        output.Content.SetContent(localizer[TextKey]);
    }
}
