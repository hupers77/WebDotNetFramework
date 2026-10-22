using System.Net;
using Portal.Core.Results;
using Portal.Data;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>역할 관리 화면: 선택지(공통코드), 버튼은 기능 권한대로, 등록·수정 구분, 저장 값 정리, 권한 없는 요청 차단.</summary>
public sealed class RoleScreenTests : IClassFixture<PortalWebFactory>
{
    private const string Program = "SYS_ROLE_MGT";
    private const string Home = "DSH_HOME:READ";
    private static readonly string[] AllFuncs = [Home, $"{Program}:READ", $"{Program}:CREATE", $"{Program}:UPDATE", $"{Program}:DELETE"];
    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 9];
    private readonly PortalWebFactory _web;

    public RoleScreenTests(PortalWebFactory web)
    {
        _web = web;
        web.Sp.Returns("FW.USP_CODE_GET_LIST", new PageResult<CodeDto>
        {
            Items = [new() { Code = "PARTNER", CodeNm = "협력사", UseYn = "Y" }, new() { Code = "DEPT_SUB", CodeNm = "소속 부서와 하위", UseYn = "Y" }],
            TotalCount = 2,
            PageNo = 1,
            PageSize = 500,
        });
        web.Sp.Returns("FW.USP_CODE_LANG_GET_LIST", new List<CodeAdminService.NameRow>());
        web.Sp.Returns(RoleAuthService.RoleListSp, new List<RoleDto> { new() { RoleId = "EMP_USER", RoleNm = "본사 일반 사용자", UserCnt = 3, RowVer = RowVer } });
        web.Sp.Returns(RoleAdminService.SaveSp, new SaveResult(1, "IT_NEW"));
        web.Sp.Returns(RoleAdminService.DeleteSp, new SaveResult(1, null));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> PageAsync(string token)
    {
        var response = await _web.NewClient(token).GetAsync("/Sys/Role?_layout=tab", Ct);
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

    private static object Body(string roleId, byte[]? rowVer) =>
        new { roleId, rowVer, roleNm = "  새 역할 ", description = " ", companyTypeCd = "", dataScopeCd = "DEPT_SUB", sortNo = 10, useYn = "Y" };

    [Fact]
    public async Task 화면_선택지는공통코드_버튼은기능권한대로()
    {
        var all = await PageAsync(_web.SignIn("rm.admin", AllFuncs));
        var reader = await PageAsync(_web.SignIn("rm.reader", [Home, $"{Program}:READ"]));

        Assert.Contains("<option value=\"DEPT_SUB\">소속 부서와 하위</option>", all, StringComparison.Ordinal);
        Assert.Contains("js/sys/role.js", all, StringComparison.Ordinal);
        foreach (var id in new[] { "role-new", "role-create", "role-save", "role-delete" })
        {
            Assert.Contains($"id=\"{id}\"", all, StringComparison.Ordinal);
            Assert.DoesNotContain($"id=\"{id}\"", reader, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task 목록_역할과부여현황()
    {
        var json = await JsonAsync(await _web.NewClient(_web.SignIn("rm.list", AllFuncs)).SendAsync(Ajax(HttpMethod.Get, "/Sys/Role/List"), Ct));

        var role = json.GetProperty("data")[0];
        Assert.Equal(("EMP_USER", 3), (role.GetProperty("roleId").GetString(), role.GetProperty("userCnt").GetInt32()));
    }

    [Fact]
    public async Task 등록_값을정리해넘기고_ROW_VER가있으면거부()
    {
        var token = _web.SignIn("rm.create", AllFuncs);

        var ok = await PostAsync(token, "/Sys/Role/Create", Body("IT_NEW", null));

        Assert.Equal("IT_NEW", (await JsonAsync(ok)).GetProperty("data").GetString());
        var p = _web.Sp.LastCall(RoleAdminService.SaveSp).Parameters;
        Assert.Equal(("IT_NEW", "새 역할", null, null, "DEPT_SUB"), ((string)p["P_ROLE_ID"]!, (string)p["P_ROLE_NM"]!, p["P_DESCRIPTION"], p["P_COMPANY_TYPE_CD"], (string)p["P_DATA_SCOPE_CD"]!));
        Assert.Equal("MSG.INVALID_INPUT", (await JsonAsync(await PostAsync(token, "/Sys/Role/Create", Body("IT_NEW", RowVer)))).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("MSG.INVALID_INPUT", (await JsonAsync(await PostAsync(token, "/Sys/Role/Save", Body("IT_NEW", null)))).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task 등록_역할ID형식이틀리면_SP를부르지않는다()
    {
        var calls = _web.Sp.Calls.Count(c => c.SpName == RoleAdminService.SaveSp);

        var response = await PostAsync(_web.SignIn("rm.bad", AllFuncs), "/Sys/Role/Create", Body("it-new", null));

        Assert.Equal("VAL.ROLE_ID_FORMAT", (await JsonAsync(response)).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(calls, _web.Sp.Calls.Count(c => c.SpName == RoleAdminService.SaveSp));
    }

    [Fact]
    public async Task 수정은수정권한_삭제는삭제권한()
    {
        var updateOnly = _web.SignIn("rm.update", [Home, $"{Program}:READ", $"{Program}:UPDATE"]);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(updateOnly, "/Sys/Role/Save", Body("EMP_USER", RowVer))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(updateOnly, "/Sys/Role/Create", Body("IT_NEW", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(updateOnly, "/Sys/Role/Delete", new { roleId = "EMP_USER", rowVer = RowVer })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(_web.SignIn("rm.delete", AllFuncs), "/Sys/Role/Delete", new { roleId = "EMP_USER", rowVer = RowVer })).StatusCode);
    }
}
