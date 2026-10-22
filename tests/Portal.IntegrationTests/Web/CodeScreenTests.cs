using System.Net;
using Portal.Core.Results;
using Portal.Data;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>공통코드 화면 (첫 표준 화면): 권한별 버튼, Area 경로, 그리드 저장의 행 상태별 권한.</summary>
public sealed class CodeScreenTests : IClassFixture<PortalWebFactory>
{
    private const string Program = "SYS_CODE_MGT";
    private const string Home = "DSH_HOME:READ";
    private static readonly string[] AllFuncs = [Home, $"{Program}:READ", $"{Program}:CREATE", $"{Program}:UPDATE", $"{Program}:DELETE"];
    private readonly PortalWebFactory _web;

    public CodeScreenTests(PortalWebFactory web)
    {
        _web = web;
        web.Sp.Returns(CodeAdminService.SaveSp, p => new SaveResult(((Tvp)p["P_ROWS"]!).Table.Rows.Count, null));
        web.Sp.Returns(CodeAdminService.ListSp, new PageResult<CodeDto>
        {
            Items = [new() { Code = "A", CodeNm = "가", RowVer = [0, 0, 0, 0, 0, 0, 0, 1] }],
            TotalCount = 1,
            PageNo = 1,
            PageSize = CodeAdminService.MaxRows,
        });
        web.Sp.Returns(CodeAdminService.NameListSp, new List<CodeAdminService.NameRow> { new() { Code = "A", LangCd = "en", CodeNm = "Ga" } });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> PageAsync(string token)
    {
        var response = await _web.NewClient(token).GetAsync("/Sys/Code?_layout=tab", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private async Task<HttpResponseMessage> SaveAsync(string token, params object[] rows)
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(token), "/");
        var request = Ajax(HttpMethod.Post, "/Sys/Code/Save", new { groupCd = "G", rows });
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await _web.NewClient(token, cookie).SendAsync(request, Ct);
    }

    private static object Row(string rowStat, string code = "A") => new
    {
        rowStat,
        code,
        names = new Dictionary<string, string> { ["ko"] = "가" },
        rowVer = rowStat == "I" ? null : "AAAAAAAAAAE=",
    };

    [Fact]
    public async Task 화면_전체권한_버튼3개_메뉴명제목_언어별컬럼정보()
    {
        var html = await PageAsync(_web.SignIn("code.admin", AllFuncs));

        Assert.Contains("<h1>메뉴9</h1>", html, StringComparison.Ordinal);
        Assert.Contains("id=\"code-add\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"code-del\"", html, StringComparison.Ordinal);
        Assert.Contains("data-func=\"UPDATE\"", html, StringComparison.Ordinal);
        Assert.Contains("zh-CN", html, StringComparison.Ordinal);
        Assert.Contains("js/sys/code.js", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<fw-button", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 화면_조회권한만_편집버튼을그리지않는다()
    {
        var html = await PageAsync(_web.SignIn("code.reader", [Home, $"{Program}:READ"]));

        Assert.DoesNotContain("id=\"code-add\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"code-del\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"code-save\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 조회권한없음_화면과목록모두403()
    {
        var token = _web.SignIn("code.none", [Home]);

        var page = await _web.NewClient(token).GetAsync("/Sys/Code?_layout=tab", Ct);
        var list = await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Sys/Code/List?groupCd=G"), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, page.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    [Fact]
    public async Task 목록_언어별명칭을함께준다()
    {
        var token = _web.SignIn("code.list", AllFuncs);

        var response = await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Sys/Code/List?groupCd=USER_STAT"), Ct);

        var json = await JsonAsync(response);
        var item = json.GetProperty("data").GetProperty("codes").GetProperty("items")[0];
        Assert.Equal("Ga", item.GetProperty("names").GetProperty("en").GetString());
        Assert.Equal("AAAAAAAAAAE=", item.GetProperty("rowVer").GetString());
        Assert.Equal("USER_STAT", _web.Sp.LastCall(CodeAdminService.ListSp).Parameters["P_GROUP_CD"]);
    }

    [Fact]
    public async Task 저장_수정권한_수정행_처리건수()
    {
        var token = _web.SignIn("code.updater", [Home, $"{Program}:READ", $"{Program}:UPDATE"]);

        var response = await SaveAsync(token, Row("U"), Row("U", "B"));

        var json = await JsonAsync(response);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(2, json.GetProperty("data").GetInt32());
    }

    [Theory]
    [InlineData("I", "CREATE")]
    [InlineData("D", "DELETE")]
    public async Task 저장_추가삭제행은_해당기능권한이없으면403_SP호출안함(string rowStat, string missing)
    {
        var funcs = AllFuncs.Where(f => f != $"{Program}:{missing}").ToArray();
        var token = _web.SignIn($"code.no-{missing}", funcs);
        var before = _web.Sp.Calls.Count(c => c.SpName == CodeAdminService.SaveSp);

        var response = await SaveAsync(token, Row("U", "B"), Row(rowStat));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("MSG.FORBIDDEN", (await JsonAsync(response)).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(before, _web.Sp.Calls.Count(c => c.SpName == CodeAdminService.SaveSp));
    }

    [Fact]
    public async Task 저장_수정권한없음_추가권한만있어도403()
    {
        var token = _web.SignIn("code.creator", [Home, $"{Program}:READ", $"{Program}:CREATE"]);

        var response = await SaveAsync(token, Row("I"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 저장_변경행없음_업무오류()
    {
        var token = _web.SignIn("code.empty", AllFuncs);

        var response = await SaveAsync(token);

        var json = await JsonAsync(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("MSG.NO_CHANGES", json.GetProperty("error").GetProperty("code").GetString());
    }
}
