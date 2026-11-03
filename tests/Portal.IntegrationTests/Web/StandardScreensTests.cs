using System.Net;
using Portal.Data;
using Portal.Web.Areas.Rpt.Models;
using Portal.Web.Areas.Rpt.Services;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;
using Portal.Web.Models;
using Portal.Web.Services;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>
/// 표준 화면 6종(내 정보·메뉴·다국어·메시지 템플릿·레포트·운영 모니터링)이 주소로 열리고(404 아님),
/// 권한이 없으면 403, 기능 권한별로 버튼이 나뉘며, 쓰기 요청은 [Func] 권한이 없으면 SP를 부르지 않고 403인지 확인한다 (DB 없이).
/// </summary>
public sealed class StandardScreensTests : IClassFixture<PortalWebFactory>
{
    private const string Home = "DSH_HOME:READ";
    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 9];
    private readonly PortalWebFactory _web;

    public StandardScreensTests(PortalWebFactory web)
    {
        _web = web;
        web.Sp.Returns(ProfileService.GetSp, new object[]
        {
            new List<ProfileDto> { new() { UserId = "u1", UserNm = "홍길동", CompanyCd = "HQ", CompanyNm = "본사", LangCd = "ko", MfaYn = "Y" } },
            new List<ProfileRoleDto> { new() { RoleId = "EMP_USER", RoleNm = "임직원" } },
        });
        web.Sp.Returns(MenuAdminService.ListSp, new object[]
        {
            new List<MenuAdminDto> { new() { MenuKey = "SYS", MenuTypeCd = "FOLDER", Depth = 1, RowVer = RowVer, Names = [] } },
            new List<MenuNameRow>(),
            new List<MenuProgramOption>(),
            null!,
        });
        web.Sp.Returns(MenuAdminService.SaveSp, new SaveResult(1, null));
        web.Sp.Returns(LangAdminService.ListSp, new object[] { new List<LangResourceRow>(), null! });
        web.Sp.Returns(LangAdminService.SaveSp, new SaveResult(1, null));
        web.Sp.Returns(MsgTemplateAdminService.ListSp, new object[] { new List<MsgTemplateDto>(), new List<MsgTemplateLangDto>() });
        web.Sp.Returns(MsgTemplateAdminService.SaveSp, new SaveResult(1, null));
        web.Sp.Returns(ReportAdminService.ListSp, new object[] { new List<ReportAdminDto>(), new List<ReportProgramOption>(), null! });
        web.Sp.Returns(ReportAdminService.SaveSp, new SaveResult(1, null));
        web.Sp.Returns(MonitorService.GetSp, new object[]
        {
            new MonitorSummary(), new List<MonitorCache>(), new List<MonitorJob>(), new List<MonitorStatus>(), new List<MonitorStatus>(), new List<MonitorError>(),
        });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string[] Funcs(string program, params string[] funcs) => [Home, .. funcs.Select(f => $"{program}:{f}")];

    private async Task<(HttpStatusCode Status, string Body)> GetAsync(string token, string path)
    {
        var response = await _web.NewClient(token).GetAsync($"{path}?_layout=tab", Ct);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    private async Task<HttpResponseMessage> PostAsync(string token, string path, object body)
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(token), "/");
        var request = Ajax(HttpMethod.Post, path, body);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await _web.NewClient(token, cookie).SendAsync(request, Ct);
    }

    public static TheoryData<string, string, string> Screens => new()
    {
        { "/My/Profile", "MY_PROFILE", "홍길동" },
        { "/Sys/Menu", "SYS_MENU_MGT", "js/sys/menu.js" },
        { "/Sys/Lang", "SYS_LANG_MGT", "js/sys/lang.js" },
        { "/Sys/MsgTemplate", "SYS_MSG_TEMPLATE_MGT", "js/sys/msg-template.js" },
        { "/Rpt/Report", "RPT_REPORT_MGT", "js/rpt/report-admin.js" },
        { "/Sys/Monitor", "SYS_MONITOR_INQ", "js/sys/monitor.js" },
    };

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task 화면이_열린다_404가_아니다(string path, string program, string marker)
    {
        var (status, body) = await GetAsync(_web.SignIn($"std.{program}", Funcs(program, "READ")), path);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains(marker, body, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task 조회권한이_없으면_403(string path, string program, string _)
    {
        var (status, _) = await GetAsync(_web.SignIn($"std.none.{program}", [Home]), path);

        Assert.Equal(HttpStatusCode.Forbidden, status);
    }

    [Fact]
    public async Task 내정보_수정권한이_있을때만_언어_저장버튼()
    {
        var reader = await GetAsync(_web.SignIn("std.pr", Funcs("MY_PROFILE", "READ")), "/My/Profile");
        var updater = await GetAsync(_web.SignIn("std.pu", Funcs("MY_PROFILE", "READ", "UPDATE")), "/My/Profile");

        Assert.DoesNotContain("data-func=\"UPDATE\"", reader.Body, StringComparison.Ordinal);
        Assert.Contains("data-func=\"UPDATE\"", updater.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("이메일", updater.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 메뉴_기능권한별_버튼()
    {
        var all = await GetAsync(_web.SignIn("std.ma", Funcs("SYS_MENU_MGT", "READ", "CREATE", "UPDATE", "DELETE")), "/Sys/Menu");
        var reader = await GetAsync(_web.SignIn("std.mr", Funcs("SYS_MENU_MGT", "READ")), "/Sys/Menu");

        foreach (var id in new[] { "menu-add", "menu-delete", "menu-save" })
        {
            Assert.Contains($"id=\"{id}\"", all.Body, StringComparison.Ordinal);
            Assert.DoesNotContain($"id=\"{id}\"", reader.Body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task 다국어_엑셀_버튼은_EXPORT_UPLOAD_권한이_있을때만()
    {
        var all = await GetAsync(_web.SignIn("std.la", Funcs("SYS_LANG_MGT", "READ", "EXPORT", "UPLOAD", "UPDATE")), "/Sys/Lang");
        var reader = await GetAsync(_web.SignIn("std.lr", Funcs("SYS_LANG_MGT", "READ")), "/Sys/Lang");

        Assert.Contains("id=\"lang-export\"", all.Body, StringComparison.Ordinal);
        Assert.Contains("id=\"lang-upload\"", all.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"lang-export\"", reader.Body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await _web.NewClient(_web.SignIn("std.lx", Funcs("SYS_LANG_MGT", "READ"))).GetAsync("/Sys/Lang/Export", Ct)).StatusCode);
    }

    [Fact]
    public async Task 템플릿_저장은_UPDATE가_없으면_403_SP를_부르지_않는다()
    {
        var token = _web.SignIn("std.tr", Funcs("SYS_MSG_TEMPLATE_MGT", "READ"));
        var before = _web.Sp.Calls.Count(c => c.SpName == MsgTemplateAdminService.SaveSp);
        var body = new { templateId = "TPL_X", rowVer = RowVer, mailYn = "Y", portalYn = "N", useYn = "Y", langs = new[] { new { langCd = "ko", subject = "s", body = "<p>b</p>" } } };

        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(token, "/Sys/MsgTemplate/Save", body)).StatusCode);
        Assert.Equal(before, _web.Sp.Calls.Count(c => c.SpName == MsgTemplateAdminService.SaveSp));
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(_web.SignIn("std.tu", Funcs("SYS_MSG_TEMPLATE_MGT", "READ", "UPDATE")), "/Sys/MsgTemplate/Save", body)).StatusCode);
    }

    [Fact]
    public async Task 행_저장은_행_상태에_맞는_기능_권한을_요구한다()
    {
        var updater = _web.SignIn("std.ru", Funcs("RPT_REPORT_MGT", "READ", "UPDATE"));
        var insert = new { rows = new[] { new { rowStat = "I", reportId = "RPT_NEW", dataSp = "LOG.USP_RPT_NEW_GET_LIST", orientCd = "P", approvalYn = "N", totalYn = "N", maxRows = 100, useYn = "Y" } } };
        var update = new { rows = new[] { new { rowStat = "U", reportId = "RPT_NEW", dataSp = "LOG.USP_RPT_NEW_GET_LIST", orientCd = "P", approvalYn = "N", totalYn = "N", maxRows = 100, useYn = "Y", rowVer = RowVer } } };

        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(updater, "/Rpt/Report/Save", insert)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(updater, "/Rpt/Report/Save", update)).StatusCode);
    }

    [Fact]
    public async Task 모니터링_데이터는_JSON으로_온다()
    {
        var token = _web.SignIn("std.mon", Funcs("SYS_MONITOR_INQ", "READ"));

        var response = await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Sys/Monitor/Data"), Ct);
        var json = await JsonAsync(response);

        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("data").GetProperty("server").GetProperty("version").GetString()));
    }

    [Fact]
    public async Task 메뉴_javascript_링크는_거부된다()
    {
        var token = _web.SignIn("std.mj", Funcs("SYS_MENU_MGT", "READ", "UPDATE", "CREATE"));
        var body = new { menuSetCd = "PORTAL", rows = new[] { new { rowStat = "I", menuKey = "EVIL", menuTypeCd = "LINK", linkUrl = "javascript:alert(1)", openScopeCd = "ALL", newWinYn = "N", useYn = "Y", names = new { ko = "x" } } } };

        var json = await JsonAsync(await PostAsync(token, "/Sys/Menu/Save", body));

        Assert.Equal("MSG.INVALID_INPUT", json.GetProperty("error").GetProperty("code").GetString());
    }
}
