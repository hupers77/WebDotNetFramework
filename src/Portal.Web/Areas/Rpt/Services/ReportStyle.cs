using Microsoft.AspNetCore.Html;

namespace Portal.Web.Areas.Rpt.Services;

/// <summary>레포트·화면 출력 문서의 CSS (<c>wwwroot/css/report.css</c>). 미리보기는 연결하고, PDF는 외부 자원이 막히므로 문서 안에 넣는다.</summary>
internal static class ReportStyle
{
    /// <summary>CSS 파일 (wwwroot 기준).</summary>
    public const string CssPath = "css/report.css";

    /// <summary>문서에 넣을 <c>&lt;style&gt;</c>. 배포된 정적 파일이라 인코딩하지 않고, 문서를 닫는 <c>&lt;/</c>만 막는다.</summary>
    public static async Task<IHtmlContent> InlineAsync(IWebHostEnvironment environment, CancellationToken cancellationToken)
    {
        var file = environment.WebRootFileProvider.GetFileInfo(CssPath);
        if (!file.Exists)
        {
            throw new InvalidOperationException($"wwwroot/{CssPath}가 없습니다.");
        }

        await using var stream = file.CreateReadStream();
        using var reader = new StreamReader(stream);
        var css = await reader.ReadToEndAsync(cancellationToken);
        return new HtmlString("<style>" + css.Replace("</", "<\\/", StringComparison.Ordinal) + "</style>");
    }
}
