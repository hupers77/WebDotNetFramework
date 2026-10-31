using Microsoft.AspNetCore.Http;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Context;
using Portal.Framework.Web.Logging;
using Portal.Framework.Web.Mvc;

namespace Portal.UnitTests.Web;

public sealed class RequestRulesTests
{
    [Theory]
    [InlineData("MSG.INVALID_INPUT", true)]
    [InlineData("VAL.REQUIRED", true)]
    [InlineData("The JSON value could not be converted to System.Int64. Path: $.id | LineNumber: 0", false)]
    [InlineData("The Code field is required.", false)]
    [InlineData("MSG", false)]
    [InlineData(".MSG.X", false)]
    public void 검증메시지는_메시지키형식만_그대로돌려준다(string message, bool expected) =>
        Assert.Equal(expected, Portal.Framework.Web.Mvc.ApiResultFilter.IsMessageKey(message));

    [Fact]
    public void PermissionResolve_Action특성이Controller특성보다우선_기능기본READ()
    {
        Assert.Equal(("B", "READ"), PermissionFilter.Resolve([new ProgramAttribute("A"), new ProgramAttribute("B")]));
        Assert.Equal(("A", "UPDATE"), PermissionFilter.Resolve([new ProgramAttribute("A"), new FuncAttribute("UPDATE")]));
        Assert.Equal((null, "READ"), PermissionFilter.Resolve([]));
    }

    [Theory]
    [InlineData(null, null, "ko")]
    [InlineData("en", "zh-CN", "en")]
    [InlineData("xx", "zh-Hans;q=0.9, fr;q=1.0", "zh-CN")]
    [InlineData(null, "en-US,en;q=0.9", "en")]
    [InlineData(null, "ko-KR", "ko")]
    [InlineData(null, "fr, de", "ko")]
    public void 비로그인언어_쿠키_브라우저언어_기본순서(string? cookie, string? acceptLanguage, string expected)
    {
        var http = new DefaultHttpContext();
        if (cookie is not null)
        {
            http.Request.Headers.Cookie = $"{HttpCurrentUser.LangCookie}={cookie}";
        }

        if (acceptLanguage is not null)
        {
            http.Request.Headers.AcceptLanguage = acceptLanguage;
        }

        Assert.Equal(expected, HttpCurrentUser.AnonymousLang(http));
    }

    [Theory]
    [InlineData("/api/v1/codes", null, null, true)]
    [InlineData("/Fw/Code/List", "XMLHttpRequest", null, true)]
    [InlineData("/Fw/Code/List", null, "application/json", true)]
    [InlineData("/Fw/Code", null, "text/html,application/xhtml+xml", false)]
    public void JSON응답대상판별(string path, string? requestedWith, string? accept, bool expected)
    {
        var http = new DefaultHttpContext();
        http.Request.Path = path;
        if (requestedWith is not null)
        {
            http.Request.Headers["X-Requested-With"] = requestedWith;
        }

        if (accept is not null)
        {
            http.Request.Headers.Accept = accept;
        }

        Assert.Equal(expected, RequestKind.WantsJson(http.Request));
    }

    [Theory]
    [InlineData("/css/portal.css", true)]
    [InlineData("/js/fw.js", true)]
    [InlineData("/health", true)]
    [InlineData("/favicon.ico", true)]
    [InlineData("/cssx", false)]
    [InlineData("/Fw/Code", false)]
    public void 접근기록제외경로(string path, bool excluded) =>
        Assert.Equal(excluded, AccessLogMiddleware.IsExcluded(path));
}
