using System.Net;
using Portal.Core.Results;
using Portal.Data;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>부서 관리 화면: 권한별 버튼, 권한 없는 요청 차단, 등록·수정 구분, 역할 목록이 SP로 가는 값, 트리 컴포넌트.</summary>
public sealed class DeptScreenTests : IClassFixture<PortalWebFactory>
{
    private const string Program = "SYS_DEPT_MGT";
    private const string Home = "DSH_HOME:READ";
    private static readonly string[] AllFuncs = [Home, $"{Program}:READ", $"{Program}:CREATE", $"{Program}:UPDATE", $"{Program}:DELETE"];
    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 9];
    private readonly PortalWebFactory _web;

    public DeptScreenTests(PortalWebFactory web)
    {
        _web = web;
        web.Sp.Returns(DeptAdminService.ListSp, new List<DeptDto> { new() { DeptId = "D000001", CompanyCd = "HQ", DeptNm = "경영지원", RowVer = RowVer } });
        web.Sp.Returns(DeptAdminService.RoleListSp, new List<DeptRoleDto> { new() { RoleId = "EMP_USER", RoleNm = "본사 일반 사용자", AssignableYn = "Y" } });
        web.Sp.Returns(DeptAdminService.SaveSp, new SaveResult(1, "D000002"));
        web.Sp.Returns(CompanyAdminService.ListSp, new PageResult<CompanyDto> { Items = [new() { CompanyCd = "HQ", CompanyNm = "본사" }, new() { CompanyCd = "P0001", CompanyNm = "협력사1" }], TotalCount = 2, PageNo = 1, PageSize = 500 });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> PageAsync(string token)
    {
        var response = await _web.NewClient(token).GetAsync("/Sys/Dept?_layout=tab", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private async Task<HttpResponseMessage> PostAsync(string token, string action, object body)
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(token), "/");
        var request = Ajax(HttpMethod.Post, $"/Sys/Dept/{action}", body);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await _web.NewClient(token, cookie).SendAsync(request, Ct);
    }

    private static object Body(byte[]? rowVer, string? deptId, params object[] roles) =>
        new { deptId, rowVer, companyCd = "HQ", parentDeptId = "", deptNm = " 인사팀 ", sortNo = 3, useYn = "Y", roles };

    [Fact]
    public async Task 화면_권한별로버튼을그리고_회사선택지와트리스크립트()
    {
        var all = await PageAsync(_web.SignIn("dept.admin", AllFuncs));
        var reader = await PageAsync(_web.SignIn("dept.reader", [Home, $"{Program}:READ"]));

        foreach (var id in new[] { "dept-new", "dept-create", "dept-save", "dept-delete" })
        {
            Assert.Contains($"id=\"{id}\"", all, StringComparison.Ordinal);
            Assert.DoesNotContain($"id=\"{id}\"", reader, StringComparison.Ordinal);
        }

        Assert.Contains("js/sys/dept.js", all, StringComparison.Ordinal);
        Assert.Contains("<option value=\"P0001\">협력사1</option>", all, StringComparison.Ordinal);
        Assert.Equal("Y", _web.Sp.LastCall(CompanyAdminService.ListSp).Parameters["P_USE_YN"]);
    }

    [Fact]
    public async Task 조회권한없음_화면목록역할모두403()
    {
        var token = _web.SignIn("dept.none", [Home]);

        Assert.Equal(HttpStatusCode.Forbidden, (await _web.NewClient(token).GetAsync("/Sys/Dept?_layout=tab", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Sys/Dept/List?companyCd=HQ"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Sys/Dept/Roles?deptId=D000001"), Ct)).StatusCode);
    }

    [Fact]
    public async Task 목록_회사와사용안함포함을넘긴다_회사없으면입력오류()
    {
        var token = _web.SignIn("dept.list", AllFuncs);

        var ok = await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Sys/Dept/List?companyCd=P0001&includeUnusedYn=Y"), Ct);
        var p = _web.Sp.LastCall(DeptAdminService.ListSp).Parameters;
        var missing = await JsonAsync(await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Sys/Dept/List"), Ct));

        Assert.Equal("D000001", (await JsonAsync(ok)).GetProperty("data")[0].GetProperty("deptId").GetString());
        Assert.Equal(("P0001", "Y"), ((string?)p["P_COMPANY_CD"], (string?)p["P_INCLUDE_UNUSED_YN"]));
        Assert.Equal("MSG.INVALID_INPUT", missing.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task 등록_새ID를돌려주고_역할은JSON으로_이름은다듬는다()
    {
        var token = _web.SignIn("dept.creator", AllFuncs);

        var json = await JsonAsync(await PostAsync(token, "Create", Body(null, null, new { roleId = "EMP_USER", inheritYn = "N" })));

        Assert.Equal("D000002", json.GetProperty("data").GetString());
        var p = _web.Sp.LastCall(DeptAdminService.SaveSp).Parameters;
        Assert.Equal(("인사팀", """[{"roleId":"EMP_USER","inheritYn":"N"}]"""), ((string)p["P_DEPT_NM"]!, (string)p["P_ROLES"]!));
        Assert.Null(p["P_DEPT_ID"]);
        Assert.Null(p["P_PARENT_DEPT_ID"]);
    }

    [Fact]
    public async Task 등록과수정_ROW_VER로구분하고_기능권한을따로본다()
    {
        var updater = _web.SignIn("dept.updater", [Home, $"{Program}:READ", $"{Program}:UPDATE"]);
        var before = _web.Sp.Calls.Count(c => c.SpName == DeptAdminService.SaveSp);

        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(updater, "Create", Body(null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(updater, "Delete", new { deptId = "D000001", rowVer = RowVer })).StatusCode);
        var wrongCreate = await JsonAsync(await PostAsync(_web.SignIn("dept.all", AllFuncs), "Create", Body(RowVer, "D000001")));
        var wrongSave = await JsonAsync(await PostAsync(updater, "Save", Body(null, "D000001")));

        Assert.Equal("MSG.INVALID_INPUT", wrongCreate.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("MSG.INVALID_INPUT", wrongSave.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(before, _web.Sp.Calls.Count(c => c.SpName == DeptAdminService.SaveSp));
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(updater, "Save", Body(RowVer, "D000001"))).StatusCode);
        Assert.Equal("D000001", _web.Sp.LastCall(DeptAdminService.SaveSp).Parameters["P_DEPT_ID"]);
    }

    [Fact]
    public async Task 저장_같은역할이두번이거나상속값이틀리면_SP를부르지않는다()
    {
        var token = _web.SignIn("dept.dup", AllFuncs);
        var before = _web.Sp.Calls.Count(c => c.SpName == DeptAdminService.SaveSp);

        var dup = await JsonAsync(await PostAsync(token, "Create", Body(null, null, new { roleId = "EMP_USER", inheritYn = "Y" }, new { roleId = "EMP_USER", inheritYn = "N" })));
        var bad = await JsonAsync(await PostAsync(token, "Create", Body(null, null, new { roleId = "EMP_USER", inheritYn = "X" })));

        Assert.Equal("MSG.INVALID_INPUT", dup.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("MSG.INVALID_INPUT", bad.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(before, _web.Sp.Calls.Count(c => c.SpName == DeptAdminService.SaveSp));
    }

    [Fact]
    public async Task 트리컴포넌트_스크립트가정적파일로나간다()
    {
        var response = await _web.NewClient(_web.SignIn("dept.js", AllFuncs)).GetAsync("/js/fw-tree.js", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("export class Tree", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }
}
