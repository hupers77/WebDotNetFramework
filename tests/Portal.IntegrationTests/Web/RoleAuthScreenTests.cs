using System.Net;
using Portal.Core.Results;
using Portal.Data;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>역할별 권한 화면: 기능 열(공통코드 FUNC_CD), 저장 버튼은 UPDATE만, 권한표 묶음, 저장 값, 권한 없는 요청 차단.</summary>
public sealed class RoleAuthScreenTests : IClassFixture<PortalWebFactory>
{
    private const string Program = "SYS_ROLE_AUTH_MGT";
    private const string Home = "DSH_HOME:READ";
    private static readonly string[] AllFuncs = [Home, $"{Program}:READ", $"{Program}:UPDATE"];
    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 9];
    private readonly PortalWebFactory _web;

    public RoleAuthScreenTests(PortalWebFactory web)
    {
        _web = web;
        web.Sp.Returns("FW.USP_CODE_GET_LIST", new PageResult<CodeDto>
        {
            Items = [new() { Code = "READ", CodeNm = "조회", UseYn = "Y" }, new() { Code = "EXPORT", CodeNm = "내보내기", UseYn = "Y" }, new() { Code = "PRINT", CodeNm = "출력", UseYn = "Y" }],
            TotalCount = 3,
            PageNo = 1,
            PageSize = 500,
        });
        web.Sp.Returns("FW.USP_CODE_LANG_GET_LIST", new List<CodeAdminService.NameRow>());
        web.Sp.Returns(RoleAuthService.RoleListSp, new List<RoleDto> { new() { RoleId = "EMP_USER", RoleNm = "본사 일반 사용자", RowVer = RowVer } });
        web.Sp.Returns(RoleAuthService.ListSp, new List<RoleAuthRow>
        {
            new() { ProgramId = "IT_NO_MENU", ProgramNm = "메뉴 없는 화면", FuncCd = "READ", GrantedYn = "Y" },
            new() { ProgramId = "IT_NO_MENU", ProgramNm = "메뉴 없는 화면", FuncCd = "PRINT", GrantedYn = "N" },
        });
        web.Sp.Returns(RoleAuthService.SaveSp, new SaveResult(2, null));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> PageAsync(string token)
    {
        var response = await _web.NewClient(token).GetAsync("/Sys/RoleAuth?_layout=tab", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private async Task<HttpResponseMessage> PostAsync(string token, object body)
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(token), "/");
        var request = Ajax(HttpMethod.Post, "/Sys/RoleAuth/Save", body);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await _web.NewClient(token, cookie).SendAsync(request, Ct);
    }

    [Fact]
    public async Task 화면_기능열은공통코드_저장버튼은수정권한만()
    {
        var all = await PageAsync(_web.SignIn("ra.admin", AllFuncs));
        var reader = await PageAsync(_web.SignIn("ra.reader", [Home, $"{Program}:READ"]));

        Assert.Contains("<th scope=\"col\" data-func=\"PRINT\">출력</th>", all, StringComparison.Ordinal);
        Assert.Contains("id=\"role-auth-save\"", all, StringComparison.Ordinal);
        Assert.Contains("js/sys/role-auth.js", all, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"role-auth-save\"", reader, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 권한표_메뉴에없는화면은따로묶고_역할ID를넘긴다()
    {
        var token = _web.SignIn("ra.list", AllFuncs);

        var json = await JsonAsync(await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Sys/RoleAuth/List?roleId=EMP_USER"), Ct));

        var program = json.GetProperty("data")[0];
        Assert.Equal(("IT_NO_MENU", "메뉴 없는 화면", "SYS.ROLE_AUTH.NO_MENU"), (program.GetProperty("programId").GetString(), program.GetProperty("programNm").GetString(), program.GetProperty("groupNm").GetString()));
        Assert.Equal(2, program.GetProperty("funcs").GetArrayLength());
        Assert.Equal("EMP_USER", _web.Sp.LastCall(RoleAuthService.ListSp).Parameters["P_ROLE_ID"]);
    }

    [Fact]
    public async Task 저장_체크한기능을JSON으로_수정권한이없으면403()
    {
        var ok = await PostAsync(_web.SignIn("ra.save", AllFuncs), new { roleId = "EMP_USER", rowVer = RowVer, funcs = new[] { new { programId = "SYS_CODE_MGT", funcCd = "READ" }, new { programId = " SYS_CODE_MGT ", funcCd = "READ" } } });

        Assert.Equal(2, (await JsonAsync(ok)).GetProperty("data").GetInt32());
        var p = _web.Sp.LastCall(RoleAuthService.SaveSp).Parameters;
        Assert.Equal(("EMP_USER", """[{"programId":"SYS_CODE_MGT","funcCd":"READ"}]"""), ((string)p["P_ROLE_ID"]!, (string)p["P_FUNCS"]!));
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(_web.SignIn("ra.reader2", [Home, $"{Program}:READ"]), new { roleId = "EMP_USER", rowVer = RowVer, funcs = Array.Empty<object>() })).StatusCode);
    }
}
