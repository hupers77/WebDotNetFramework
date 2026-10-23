using Portal.Framework.Web.Api;

namespace Portal.UnitTests.Api;

public sealed class ApiLogMaskerTests
{
    [Fact]
    public void JSON의_민감필드는_깊은곳도_가린다()
    {
        var masked = ApiLogMasker.MaskBody("""{"orderNo":"A1","password":"p","items":[{"qty":1,"cardNo":"1234"}],"user":{"clientSecret":"s","name":"kim"}}""", "application/json; charset=utf-8");

        Assert.NotNull(masked);
        Assert.Contains("\"orderNo\":\"A1\"", masked, StringComparison.Ordinal);
        Assert.Contains("\"qty\":1", masked, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"kim\"", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("1234", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("\"p\"", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("\"s\"", masked, StringComparison.Ordinal);
    }

    [Fact]
    public void 응답의_access_token도_가린다()
    {
        var masked = ApiLogMasker.MaskBody("""{"access_token":"abc.def.ghi","expires_in":1800}""", "application/json");

        Assert.DoesNotContain("abc.def.ghi", masked, StringComparison.Ordinal);
        Assert.Contains("1800", masked, StringComparison.Ordinal);
    }

    [Fact]
    public void 폼_본문은_이름기준으로_가린다()
    {
        var masked = ApiLogMasker.MaskBody("grant_type=client_credentials&client_id=erp&client_secret=topsecret", "application/x-www-form-urlencoded");

        Assert.Contains("client_id=erp", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("topsecret", masked, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<xml>x</xml>", "text/xml")]
    [InlineData("binary", null)]
    [InlineData("{not json", "application/json")]
    public void 해석할수없거나_JSON폼이아니면_남기지않는다(string body, string? contentType) =>
        Assert.Equal(ApiLogMasker.Omitted, ApiLogMasker.MaskBody(body, contentType));

    [Fact]
    public void 빈본문은_null() => Assert.Null(ApiLogMasker.MaskBody("", "application/json"));

    [Fact]
    public void 긴본문은_잘린다()
    {
        var masked = ApiLogMasker.MaskBody("{\"memo\":\"" + new string('가', 6000) + "\"}", "application/json");

        Assert.NotNull(masked);
        Assert.True(masked.Length <= ApiLogMasker.MaxBodyLength + 1);
    }

    [Fact]
    public void URL의_쿼리값도_민감하면_가린다()
    {
        var url = ApiLogMasker.MaskUrl("/api/v1/orders", "?orderNo=A1&access_token=zzz");

        Assert.Equal("/api/v1/orders?orderNo=A1&access_token=***", url);
        Assert.Equal("/api/v1/ping", ApiLogMasker.MaskUrl("/api/v1/ping", null));
        Assert.Equal("/api/v1/ping", ApiLogMasker.MaskUrl("/api/v1/ping", "?"));
    }
}
