using Portal.Infrastructure.Html;

namespace Portal.UnitTests.Infrastructure;

public sealed class HtmlSanitizerTests
{
    [Theory]
    [InlineData("<p>안녕</p>", "<p>안녕</p>")]
    [InlineData("<b>굵게</b> <i>기울임</i>", "<b>굵게</b> <i>기울임</i>")]
    [InlineData("줄<br>바꿈", "줄<br />바꿈")]
    [InlineData("<ul><li>하나</li><li>둘</li></ul>", "<ul><li>하나</li><li>둘</li></ul>")]
    [InlineData("<table><tr><td colspan=\"2\">값</td></tr></table>", "<table><tr><td colspan=\"2\">값</td></tr></table>")]
    public void 허용태그는_그대로(string input, string expected) =>
        Assert.Equal(expected, HtmlSanitizer.Sanitize(input));

    [Theory]
    [InlineData("<script>alert(1)</script>본문", "본문")]
    [InlineData("<SCRIPT SRC=//evil/x.js></SCRIPT>본문", "본문")]
    [InlineData("<style>body{display:none}</style>본문", "본문")]
    [InlineData("<iframe src=\"https://evil\"></iframe>본문", "본문")]
    [InlineData("<svg onload=alert(1)><circle/></svg>본문", "본문")]
    [InlineData("<script>alert(1)", "")]
    public void 위험한태그는_내용까지_지운다(string input, string expected) =>
        Assert.Equal(expected, HtmlSanitizer.Sanitize(input));

    [Theory]
    [InlineData("<p onclick=\"alert(1)\">글</p>", "<p>글</p>")]
    [InlineData("<p style=\"background:url(javascript:alert(1))\">글</p>", "<p>글</p>")]
    [InlineData("<img src=x onerror=alert(1)>글", "글")]
    [InlineData("<b onmouseover=alert(1)>글</b>", "<b>글</b>")]
    [InlineData("<div class=\"x\" id=\"y\">글</div>", "<div>글</div>")]
    public void 허용하지않은_속성과_태그는_지운다(string input, string expected) =>
        Assert.Equal(expected, HtmlSanitizer.Sanitize(input));

    [Theory]
    [InlineData("<a href=\"javascript:alert(1)\">링크</a>")]
    [InlineData("<a href=\"JaVaScRiPt:alert(1)\">링크</a>")]
    [InlineData("<a href=\"java\tscript:alert(1)\">링크</a>")]
    [InlineData("<a href=\"&#106;avascript:alert(1)\">링크</a>")]
    [InlineData("<a href=\"data:text/html,<script>alert(1)</script>\">링크</a>")]
    [InlineData("<a href=\"vbscript:msgbox(1)\">링크</a>")]
    [InlineData("<a href=\"//evil.example/x\">링크</a>")]
    public void 위험한링크는_주소를_뺀다(string input) =>
        Assert.Equal("<a>링크</a>", HtmlSanitizer.Sanitize(input));

    [Theory]
    [InlineData("https://example.com/a?b=1&c=2", "https://example.com/a?b=1&amp;c=2")]
    [InlineData("mailto:a@example.com", "mailto:a@example.com")]
    [InlineData("/Board/Notice/View/3", "/Board/Notice/View/3")]
    public void 허용한링크는_새창과_rel을_붙인다(string href, string expectedHref) =>
        Assert.Equal($"<a href=\"{expectedHref}\" rel=\"noopener noreferrer\" target=\"_blank\">글</a>", HtmlSanitizer.Sanitize($"<a href=\"{href}\">글</a>"));

    [Fact]
    public void 닫히지않은_태그는_닫는다() =>
        Assert.Equal("<p><b>굵게</b></p>", HtmlSanitizer.Sanitize("<p><b>굵게"));

    [Fact]
    public void 짝이없는_닫는태그는_버린다() =>
        Assert.Equal("글", HtmlSanitizer.Sanitize("</b>글</div>"));

    [Fact]
    public void 서로엇갈린_태그는_바깥을_닫으면서_안쪽도_닫는다() =>
        Assert.Equal("<b><i>글</i></b>글", HtmlSanitizer.Sanitize("<b><i>글</b>글</i>"));

    [Theory]
    [InlineData("1 < 2", "1 &lt; 2")]
    [InlineData("a > b", "a &gt; b")]
    [InlineData("A & B", "A &amp; B")]
    [InlineData("&lt;script&gt;", "&lt;script&gt;")]
    [InlineData("&nbsp;&#65;", "&nbsp;&#65;")]
    [InlineData("<<script>alert(1)</script>", "&lt;")]
    public void 글자의_특수문자는_인코딩한다(string input, string expected) =>
        Assert.Equal(expected, HtmlSanitizer.Sanitize(input));

    [Fact]
    public void 주석과_제어문자는_버린다() =>
        Assert.Equal("글", HtmlSanitizer.Sanitize("<!-- 숨김 -->글\0\u0001"));

    [Fact]
    public void 속성_따옴표를_닫고_이벤트를_끼우는_시도는_통하지않는다()
    {
        var result = HtmlSanitizer.Sanitize("<a href=\"https://a.example\" onclick=\"x\" \"onfocus=alert(1)\">글</a>");

        Assert.DoesNotContain("onclick", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onfocus", result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("<a href=\"https://a.example\" rel=\"noopener noreferrer\" target=\"_blank\">글</a>", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void 빈값은_빈문자열(string? input) => Assert.Equal("", HtmlSanitizer.Sanitize(input));

    [Fact]
    public void 평문으로_바꾸면_태그가_빠지고_엔티티가_풀린다() =>
        Assert.Equal("제목 본문 A & B", HtmlSanitizer.ToPlainText("<h1>제목</h1><p>본문 <b>A &amp; B</b></p><script>x</script>"));
}
