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
}
