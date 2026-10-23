using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Portal.Web.Areas.Rpt.Services;

/// <summary>레포트 템플릿(Razor 뷰)을 HTML 문자열로 만든다. 미리보기와 PDF가 같은 HTML을 쓴다.</summary>
public sealed class ReportRenderer(IRazorViewEngine engine, ITempDataProvider tempData, IModelMetadataProvider metadata)
{
    /// <summary>뷰 경로(<c>~/Areas/Rpt/Views/Templates/Default.cshtml</c>)를 모델로 그린다.</summary>
    public async Task<string> RenderAsync(ActionContext context, string viewPath, object model)
    {
        var found = engine.GetView(executingFilePath: null, viewPath, isMainPage: true);
        if (!found.Success)
        {
            throw new InvalidOperationException($"레포트 템플릿 '{viewPath}'이 없습니다.");
        }

        await using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var viewData = new ViewDataDictionary(metadata, new ModelStateDictionary()) { Model = model };
        var viewContext = new ViewContext(context, found.View, viewData, new TempDataDictionary(context.HttpContext, tempData), writer, new HtmlHelperOptions());
        await found.View.RenderAsync(viewContext);
        return writer.ToString();
    }
}
