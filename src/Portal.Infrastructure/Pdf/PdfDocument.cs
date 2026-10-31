using System.Net;
using System.Text.RegularExpressions;

namespace Portal.Infrastructure.Pdf;

/// <summary>변환 전에 HTML을 다듬고 브라우저 인쇄 옵션을 만든다(브라우저 없이 단위 테스트한다).</summary>
internal static partial class PdfDocument
{
    /// <summary>
    /// 외부 자원 차단 정책. 문서 안의 스타일과 data: 그림·글꼴만 허용하고 스크립트는 막는다.
    /// 사용자 입력이 HTML에 섞여도 로컬 파일이나 내부 주소를 불러오지 못하게 한다(브라우저의 프록시 차단과 이중으로 막는다).
    /// </summary>
    internal const string ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src data:";

    private const string CspMeta = "<meta http-equiv=\"Content-Security-Policy\" content=\"" + ContentSecurityPolicy + "\">";

    /// <summary>문서 맨 앞(<c>&lt;head&gt;</c> 바로 뒤, 없으면 맨 앞)에 차단 정책을 넣는다. 정책은 먼저 나온 것이 적용되므로 문서가 바꿀 수 없다.</summary>
    internal static string Prepare(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        // <header> 같은 태그를 <head>로 잘못 보면 CSP가 본문에 들어가 무시되므로 이름이 정확히 head인 태그만 찾는다
        var head = HeadTag().Match(html) is { Success: true } m ? m.Index : -1;
        if (head >= 0)
        {
            var close = html.IndexOf('>', head);
            if (close > head)
            {
                return string.Concat(html.AsSpan(0, close + 1), CspMeta, html.AsSpan(close + 1));
            }
        }

        return "<!doctype html><html><head><meta charset=\"utf-8\">" + CspMeta + "</head><body>" + html + "</body></html>";
    }

    /// <summary>쪽 아래 공통 바닥글: 왼쪽에 문구(HTML 인코딩), 오른쪽에 "쪽 / 전체 쪽".</summary>
    internal static string FooterTemplate(string? footerText) =>
        "<div style=\"font-size:8px;color:#555;width:100%;padding:0 10mm;display:flex;justify-content:space-between;font-family:'Malgun Gothic','Microsoft YaHei',sans-serif\">"
        + "<span>" + WebUtility.HtmlEncode(footerText ?? "") + "</span>"
        + "<span><span class=\"pageNumber\"></span> / <span class=\"totalPages\"></span></span></div>";

    /// <summary>
    /// <c>Page.printToPDF</c> 인자. A4, 여백(인치) 위·좌·우 0.4 / 아래 0.6(바닥글 자리), 배경 인쇄,
    /// 문서가 <c>@page</c>로 크기를 정하면 그것을 따른다.
    /// </summary>
    internal static Dictionary<string, object> PrintParameters(PdfPageOptions options) => new()
    {
        ["landscape"] = options.Landscape,
        ["printBackground"] = true,
        ["preferCSSPageSize"] = true,
        ["paperWidth"] = 8.27,
        ["paperHeight"] = 11.69,
        ["marginTop"] = 0.4,
        ["marginBottom"] = 0.6,
        ["marginLeft"] = 0.4,
        ["marginRight"] = 0.4,
        ["displayHeaderFooter"] = true,
        ["headerTemplate"] = "<span></span>",
        ["footerTemplate"] = FooterTemplate(options.FooterText),
    };

    /// <summary>
    /// 헤드리스 브라우저 실행 인자. 네트워크는 닿지 않는 프록시로 모두 막고(로컬 주소 포함), 확장·동기화·백그라운드 통신을 끈다.
    /// 제어는 로컬 디버깅 포트(임의 포트, 127.0.0.1)로 하고, 프로필은 변환마다 새 임시 폴더를 쓴다.
    /// </summary>
    internal static IReadOnlyList<string> BrowserArguments(string userDataDir, bool noSandbox)
    {
        var args = new List<string>
        {
            "--headless=new",
            "--disable-gpu",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-extensions",
            "--disable-sync",
            "--disable-background-networking",
            "--disable-component-update",
            "--mute-audio",
            "--proxy-server=127.0.0.1:9",
            "--proxy-bypass-list=<-loopback>",
            "--remote-debugging-address=127.0.0.1",
            "--remote-debugging-port=0",
            "--user-data-dir=" + userDataDir,
        };
        if (noSandbox)
        {
            args.Add("--no-sandbox");
        }

        args.Add("about:blank");
        return args;
    }

    [GeneratedRegex(@"<head(?=[\s>/])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeadTag();
}
