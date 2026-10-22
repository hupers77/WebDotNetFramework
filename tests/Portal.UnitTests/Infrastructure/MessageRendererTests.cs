using Portal.Infrastructure.Messaging;

namespace Portal.UnitTests.Infrastructure;

public sealed class MessageRendererTests
{
    private static readonly Dictionary<string, string?> Values = new(StringComparer.Ordinal)
    {
        ["USER_NM"] = "<b>홍</b> & \"길동\"",
        ["LINK"] = "https://portal.example.com/",
        ["EMPTY"] = null,
    };

    [Fact]
    public void 본문_값은HTML인코딩_템플릿태그는그대로()
    {
        var html = MessageRenderer.RenderBody("<p>{{USER_NM}}님</p><a href=\"{{LINK}}\">{{LINK}}</a>", Values);

        Assert.Equal("<p>&lt;b&gt;홍&lt;/b&gt; &amp; &quot;길동&quot;님</p><a href=\"https://portal.example.com/\">https://portal.example.com/</a>", html);
    }

    [Fact]
    public void 없는변수와_빈값은_빈칸_형식이아닌괄호는그대로()
    {
        Assert.Equal("[] [] {{lower}} {USER_NM}", MessageRenderer.RenderBody("[{{NONE}}] [{{EMPTY}}] {{lower}} {USER_NM}", Values));
    }

    [Fact]
    public void 제목_줄바꿈과제어문자를지워_헤더주입을막는다()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal) { ["USER_NM"] = "홍\r\nBcc: evil@example.com\t" };

        var subject = MessageRenderer.RenderSubject(" [포털] {{USER_NM}} ", values);

        Assert.Equal("[포털] 홍Bcc: evil@example.com", subject);
        Assert.DoesNotContain('\n', subject);
    }

    [Fact]
    public void 값안의치환변수는_다시치환하지않는다()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal) { ["A"] = "{{B}}", ["B"] = "x" };

        Assert.Equal("{{B}}", MessageRenderer.RenderBody("{{A}}", values));
    }

    [Fact]
    public void 글자만_문단과줄바꿈은줄로_태그는지우고_엔터티는푼다()
    {
        var html = "<p>작업 <strong>A&amp;B</strong>가 실패했습니다.</p><p>사유: &lt;5&gt;<br/>다시 확인</p><p><a href=\"\"></a></p>  ";

        Assert.Equal("작업 A&B가 실패했습니다.\n사유: <5>\n다시 확인", MessageRenderer.ToPlainText(html));
    }

    [Fact]
    public void 치환값JSON_문자열숫자null을읽고_객체가아니면비어있다()
    {
        var values = MessageRenderer.ParseValues("""{"A":"x","N":3,"Z":null}""");

        Assert.Equal(("x", "3", (string?)null), (values["A"], values["N"], values["Z"]));
        Assert.Empty(MessageRenderer.ParseValues(null));
        Assert.Empty(MessageRenderer.ParseValues("[1,2]"));
    }
}
