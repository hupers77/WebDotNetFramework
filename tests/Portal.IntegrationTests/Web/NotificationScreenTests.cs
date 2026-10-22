using System.Net;
using Portal.Core.Results;
using Portal.Infrastructure.Messaging;
using Portal.Security.Authentication;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>
/// 알림함 화면과 헤더 알림 배지: 권한별 배지·삭제 버튼, 배지 조회는 세션을 연장하지 않고 접속기록을 남기지 않는다, 삭제 건수 제한.
/// </summary>
public sealed class NotificationScreenTests : IClassFixture<PortalWebFactory>
{
    private const string Program = "MY_NOTIFICATION";
    private const string Home = "DSH_HOME:READ";
    private readonly PortalWebFactory _web;

    public NotificationScreenTests(PortalWebFactory web)
    {
        _web = web;
        web.Sp.Returns(NotificationService.UnreadSp, new NotificationService.UnreadRow { UnreadCnt = 3 });
        web.Sp.Returns(NotificationService.ListSp, new PageResult<NotificationService.NotificationRow>
        {
            Items =
            [
                new() { NotiId = 7, TemplateId = "TPL_JOB_FAILED", Subject = "[{{SITE_NM}}] 배치 작업 실패: {{JOB_NM}}", Body = "<p>작업 <strong>{{JOB_NM}}</strong></p><p><a href=\"{{LINK}}\">{{LINK}}</a></p>", ParamsJson = """{"JOB_NM":"<메일>"}""", LinkProgramId = "SYS_JOB_MGT", LinkUrl = "/Sys/Job" },
            ],
            TotalCount = 1,
            PageNo = 1,
            PageSize = NotificationService.PageSize,
        });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> GetHtmlAsync(string token, string url)
    {
        var response = await _web.NewClient(token).GetAsync(url, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private async Task<HttpResponseMessage> PostAsync(string token, string action, object body)
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(token), "/");
        var request = Ajax(HttpMethod.Post, $"/My/Notification/{action}", body);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await _web.NewClient(token, cookie).SendAsync(request, Ct);
    }

    [Fact]
    public async Task 헤더_알림권한이있으면배지_없으면없다()
    {
        var with = await GetHtmlAsync(_web.SignIn("noti.bell", [Home, $"{Program}:READ"]), "/");
        var without = await GetHtmlAsync(_web.SignIn("noti.nobell", [Home]), "/");

        Assert.Contains("id=\"noti-bell\"", with, StringComparison.Ordinal);
        Assert.Contains("data-url-unread=\"/My/Notification/Unread\"", with, StringComparison.Ordinal);
        Assert.Contains("data-poll-seconds=\"60\"", with, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"noti-bell\"", without, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 화면_삭제권한이있을때만삭제버튼()
    {
        var all = await GetHtmlAsync(_web.SignIn("noti.all", [Home, $"{Program}:READ", $"{Program}:DELETE"]), "/My/Notification?_layout=tab");
        var reader = await GetHtmlAsync(_web.SignIn("noti.reader", [Home, $"{Program}:READ"]), "/My/Notification?_layout=tab");

        Assert.Contains("id=\"noti-delete\"", all, StringComparison.Ordinal);
        Assert.Contains("js/my/notification.js", all, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"noti-delete\"", reader, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 배지조회_백그라운드요청은세션을연장하지않고_접속기록을남기지않는다()
    {
        var token = _web.SignIn("noti.poll", [Home, $"{Program}:READ"]);
        var request = Ajax(HttpMethod.Get, "/My/Notification/Unread");
        request.Headers.Add("X-Portal-Background", "1");

        var response = await _web.NewClient(token).SendAsync(request, Ct);

        Assert.Equal(3, (await JsonAsync(response)).GetProperty("data").GetProperty("count").GetInt32());
        var hash = SessionToken.Hash(token);
        Assert.Equal("N", _web.Sp.Calls.Last(c => c.SpName == "FW.USP_AUTH_SESSION_CHECK_EXEC" && (string?)c.Parameters["P_SESSION_HASH"] == hash).Parameters["P_TOUCH_YN"]);
        Assert.DoesNotContain(_web.DrainAccessLog(), e => e.UserId == "noti.poll");
    }

    [Fact]
    public async Task 목록은보통요청_세션을연장하고접속기록을남긴다_문구는치환하고글자만()
    {
        var token = _web.SignIn("noti.list", [Home, $"{Program}:READ"]);

        var json = await JsonAsync(await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/My/Notification/List?unreadYn=Y&pageNo=1"), Ct));

        var item = json.GetProperty("data").GetProperty("items")[0];
        Assert.Equal("배치 작업 실패: <메일>", item.GetProperty("title").GetString());
        Assert.Equal("작업 <메일>", item.GetProperty("content").GetString());
        Assert.Equal("/Sys/Job", item.GetProperty("linkUrl").GetString());
        Assert.Equal("Y", _web.Sp.LastCall(NotificationService.ListSp).Parameters["P_UNREAD_YN"]);
        var hash = SessionToken.Hash(token);
        Assert.Equal("Y", _web.Sp.Calls.Last(c => c.SpName == "FW.USP_AUTH_SESSION_CHECK_EXEC" && (string?)c.Parameters["P_SESSION_HASH"] == hash).Parameters["P_TOUCH_YN"]);
        Assert.Contains(_web.DrainAccessLog(), e => e.UserId == "noti.list" && e.ProgramId == Program);
    }

    [Fact]
    public async Task 읽음과삭제_SP를부르고_삭제는권한과건수를확인한다()
    {
        var reader = _web.SignIn("noti.r", [Home, $"{Program}:READ"]);
        var deleter = _web.SignIn("noti.d", [Home, $"{Program}:READ", $"{Program}:DELETE"]);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(reader, "Read", new { notiId = 7 })).StatusCode);
        Assert.Equal(7L, _web.Sp.LastCall(NotificationService.ReadSp).Parameters["P_NOTI_ID"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(reader, "Delete", new { notiIds = new[] { 7 } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(deleter, "Delete", new { notiIds = new[] { 7, 8 } })).StatusCode);
        Assert.Equal("[7,8]", _web.Sp.LastCall(NotificationService.DeleteSp).Parameters["P_NOTI_IDS"]);

        var tooMany = await PostAsync(deleter, "Delete", new { notiIds = Enumerable.Range(1, NotificationService.MaxDelete + 1).ToArray() });
        Assert.Equal("MSG.INVALID_INPUT", (await JsonAsync(tooMany)).GetProperty("error").GetProperty("code").GetString());
    }
}
