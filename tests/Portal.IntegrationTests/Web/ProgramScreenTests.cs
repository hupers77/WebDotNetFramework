using System.Net;
using Portal.Core.Results;
using Portal.Data;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>프로그램 관리 화면: 기능 이름(공통코드), 버튼은 기능 권한대로, 등록만 모듈·URL을 넘김, 기능 목록 정리, 권한 없는 요청 차단.</summary>
public sealed class ProgramScreenTests : IClassFixture<PortalWebFactory>
{
    private const string Program = "SYS_PROGRAM_MGT";
    private const string Home = "DSH_HOME:READ";
    private static readonly string[] AllFuncs = [Home, $"{Program}:READ", $"{Program}:CREATE", $"{Program}:UPDATE", $"{Program}:DELETE"];
    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 9];
    private readonly PortalWebFactory _web;

    public ProgramScreenTests(PortalWebFactory web)
    {
        _web = web;
        web.Sp.Returns("FW.USP_CODE_GET_LIST", new PageResult<CodeDto>
        {
            Items = [new() { Code = "READ", CodeNm = "조회", UseYn = "Y" }, new() { Code = "PRINT", CodeNm = "출력", UseYn = "Y" }, new() { Code = "OLD", CodeNm = "옛 기능", UseYn = "N" }],
            TotalCount = 3,
            PageNo = 1,
            PageSize = 500,
        });
        web.Sp.Returns("FW.USP_CODE_LANG_GET_LIST", new List<CodeAdminService.NameRow>());
        web.Sp.Returns(ProgramAdminService.ListSp, new List<ProgramDto> { new() { ProgramId = "SYS_USER_MGT", ProgramNm = "사용자 관리", Funcs = "READ,EXPORT", ApiCnt = 9, RowVer = RowVer } });
        web.Sp.Returns(ProgramAdminService.FuncListSp, new List<ProgramFuncDto> { new() { FuncCd = "READ", UsedYn = "Y", ApiCnt = 3 } });
        web.Sp.Returns(ProgramAdminService.SaveSp, new SaveResult(1, "IT_NEW"));
        web.Sp.Returns(ProgramAdminService.DeleteSp, new SaveResult(1, null));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> PageAsync(string token)
    {
        var response = await _web.NewClient(token).GetAsync("/Sys/Program?_layout=tab", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private async Task<HttpResponseMessage> PostAsync(string token, string path, object body)
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(token), "/");
        var request = Ajax(HttpMethod.Post, path, body);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await _web.NewClient(token, cookie).SendAsync(request, Ct);
    }

    private static object Body(byte[]? rowVer, string programId = "IT_NEW", string url = "/It/New", string[]? funcs = null) =>
        new { programId, rowVer, moduleCd = "IT", url, programNm = " 새 화면 ", piiYn = "N", useYn = "Y", funcs = funcs ?? [" READ ", "PRINT"] };

    private static async Task<string?> ErrorAsync(HttpResponseMessage response) =>
        (await JsonAsync(response)).GetProperty("error").GetProperty("code").GetString();

    [Fact]
    public async Task 화면_사용중인기능이름만_버튼은기능권한대로()
    {
        var all = await PageAsync(_web.SignIn("pg.admin", AllFuncs));
        var reader = await PageAsync(_web.SignIn("pg.reader", [Home, $"{Program}:READ"]));

        Assert.Contains("js/sys/program.js", all, StringComparison.Ordinal);
        Assert.Contains("PRINT", all, StringComparison.Ordinal);
        Assert.DoesNotContain("&quot;OLD&quot;", all, StringComparison.Ordinal);
        foreach (var id in new[] { "program-new", "program-create", "program-save", "program-delete" })
        {
            Assert.Contains($"id=\"{id}\"", all, StringComparison.Ordinal);
            Assert.DoesNotContain($"id=\"{id}\"", reader, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task 목록과기능선택지()
    {
        var client = _web.NewClient(_web.SignIn("pg.list", AllFuncs));

        var list = await JsonAsync(await client.SendAsync(Ajax(HttpMethod.Get, "/Sys/Program/List"), Ct));
        var funcs = await JsonAsync(await client.SendAsync(Ajax(HttpMethod.Get, "/Sys/Program/Funcs?programId=SYS_USER_MGT"), Ct));

        Assert.Equal(9, list.GetProperty("data")[0].GetProperty("apiCnt").GetInt32());
        Assert.Equal(3, funcs.GetProperty("data")[0].GetProperty("apiCnt").GetInt32());
        Assert.Equal("SYS_USER_MGT", _web.Sp.LastCall(ProgramAdminService.FuncListSp).Parameters["P_PROGRAM_ID"]);
    }

    [Fact]
    public async Task 등록_모듈URL을넘기고_기능은정리한JSON()
    {
        var ok = await PostAsync(_web.SignIn("pg.create", AllFuncs), "/Sys/Program/Create", Body(null));

        Assert.Equal("IT_NEW", (await JsonAsync(ok)).GetProperty("data").GetString());
        var p = _web.Sp.LastCall(ProgramAdminService.SaveSp).Parameters;
        Assert.Equal(("IT", "/It/New", "새 화면", """["READ","PRINT"]"""), ((string)p["P_MODULE_CD"]!, (string)p["P_URL"]!, (string)p["P_PROGRAM_NM"]!, (string)p["P_FUNCS"]!));
    }

    [Fact]
    public async Task 수정_모듈URL은넘기지않는다()
    {
        await PostAsync(_web.SignIn("pg.update", AllFuncs), "/Sys/Program/Save", Body(RowVer, "SYS_USER_MGT", "/Other"));

        var p = _web.Sp.LastCall(ProgramAdminService.SaveSp).Parameters;
        Assert.Equal(("SYS_USER_MGT", null, null), ((string)p["P_PROGRAM_ID"]!, p["P_MODULE_CD"], p["P_URL"]));
    }

    [Fact]
    public async Task 잘못된요청은_SP를부르지않는다()
    {
        var token = _web.SignIn("pg.bad", AllFuncs);
        var calls = _web.Sp.Calls.Count(c => c.SpName == ProgramAdminService.SaveSp);

        Assert.Equal("VAL.PROGRAM_URL_FORMAT", await ErrorAsync(await PostAsync(token, "/Sys/Program/Create", Body(null, url: "//evil.example"))));
        Assert.Equal("VAL.PROGRAM_ID_FORMAT", await ErrorAsync(await PostAsync(token, "/Sys/Program/Create", Body(null, programId: "it-new"))));
        Assert.Equal("MSG.INVALID_INPUT", await ErrorAsync(await PostAsync(token, "/Sys/Program/Create", Body(null, funcs: ["READ", "READ"]))));
        Assert.Equal("MSG.INVALID_INPUT", await ErrorAsync(await PostAsync(token, "/Sys/Program/Create", Body(RowVer))));
        Assert.Equal("MSG.INVALID_INPUT", await ErrorAsync(await PostAsync(token, "/Sys/Program/Save", Body(null))));
        Assert.Equal(calls, _web.Sp.Calls.Count(c => c.SpName == ProgramAdminService.SaveSp));
    }

    [Fact]
    public async Task 수정권한만있으면_등록삭제는403()
    {
        var updateOnly = _web.SignIn("pg.upd", [Home, $"{Program}:READ", $"{Program}:UPDATE"]);

        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(updateOnly, "/Sys/Program/Create", Body(null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(updateOnly, "/Sys/Program/Delete", new { programId = "IT_NEW", rowVer = RowVer })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(_web.SignIn("pg.del", AllFuncs), "/Sys/Program/Delete", new { programId = "IT_NEW", rowVer = RowVer })).StatusCode);
    }
}
