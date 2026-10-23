using Portal.Infrastructure.Pdf;

namespace Portal.UnitTests.Infrastructure;

public sealed class PdfDocumentTests
{
    [Fact]
    public void 차단정책은_head바로뒤에들어가_문서의정책보다먼저적용된다()
    {
        var html = PdfDocument.Prepare("<!doctype html><html><HEAD lang=\"ko\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src *\"></HEAD><body>x</body></html>");

        Assert.StartsWith("<!doctype html><html><HEAD lang=\"ko\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none';", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("default-src 'none'", StringComparison.Ordinal) < html.IndexOf("default-src *", StringComparison.Ordinal));
    }

    [Fact]
    public void head가없는조각은_문서로감싼다()
    {
        var html = PdfDocument.Prepare("<p>본문</p>");

        Assert.StartsWith("<!doctype html><html><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\"", html, StringComparison.Ordinal);
        Assert.EndsWith("<body><p>본문</p></body></html>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void 차단정책_스크립트와외부자원을막고_문서안스타일과data그림만허용()
    {
        Assert.Equal("default-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src data:", PdfDocument.ContentSecurityPolicy);
    }

    [Fact]
    public void 바닥글_문구는HTML인코딩하고_쪽번호를붙인다()
    {
        var footer = PdfDocument.FooterTemplate("출력: <admin> 2026-10-23 11:00");

        Assert.Contains("<span>출력: &lt;admin&gt; 2026-10-23 11:00</span>", footer, StringComparison.Ordinal);
        Assert.Contains("<span class=\"pageNumber\"></span> / <span class=\"totalPages\"></span>", footer, StringComparison.Ordinal);
        Assert.Contains("<span></span>", PdfDocument.FooterTemplate(null), StringComparison.Ordinal);
    }

    [Fact]
    public void 인쇄옵션_A4여백배경_가로방향과바닥글()
    {
        var p = PdfDocument.PrintParameters(new PdfPageOptions("출력자", Landscape: true));

        Assert.Equal(true, p["landscape"]);
        Assert.Equal(8.27, p["paperWidth"]);
        Assert.Equal(11.69, p["paperHeight"]);
        Assert.Equal(0.6, p["marginBottom"]);
        Assert.Equal(true, p["printBackground"]);
        Assert.Equal(true, p["displayHeaderFooter"]);
        Assert.Contains("출력자", (string)p["footerTemplate"], StringComparison.Ordinal);
        Assert.Equal(false, PdfDocument.PrintParameters(new PdfPageOptions())["landscape"]);
    }

    [Fact]
    public void 브라우저인자_네트워크차단_로컬디버깅포트_임시프로필()
    {
        var args = PdfDocument.BrowserArguments("/tmp/p1", noSandbox: false);

        Assert.Contains("--headless=new", args);
        Assert.Contains("--proxy-server=127.0.0.1:9", args);
        Assert.Contains("--proxy-bypass-list=<-loopback>", args);
        Assert.Contains("--remote-debugging-address=127.0.0.1", args);
        Assert.Contains("--remote-debugging-port=0", args);
        Assert.Contains("--user-data-dir=/tmp/p1", args);
        Assert.DoesNotContain("--no-sandbox", args);
        Assert.Equal("about:blank", args[^1]);
        Assert.Contains("--no-sandbox", PdfDocument.BrowserArguments("/tmp/p1", noSandbox: true));
    }
}
