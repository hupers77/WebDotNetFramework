using System.Net;
using Portal.Core.Results;
using Portal.Data;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>사용자 관리 화면: 권한별 버튼(UPDATE), 권한 없는 요청 차단, 저장·잠금 해제·2단계 인증 초기화가 SP로 가는 값, 개인정보 미노출, 협력사 관리자 화면(Ptn/User).</summary>
public sealed class UserScreenTests : IClassFixture<PortalWebFactory>
{
    private const string Program = "SYS_USER_MGT";
    private const string Home = "DSH_HOME:READ";
    private static readonly string[] AllFuncs = [Home, $"{Program}:READ", $"{Program}:UPDATE"];
    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 7];
    private readonly PortalWebFactory _web;

    public UserScreenTests(PortalWebFactory web)
    {
        _web = web;
        web.Sp.Returns(UserAdminService.ListSp, new PageResult<UserAdminDto>
        {
            Items = [new() { UserId = "kim.ptn", UserNm = "김협력", CompanyCd = "P0001", CompanyNm = "협력사1", UserTypeCd = "PARTNER", UserStatCd = "ACTIVE", RowVer = RowVer }],
            TotalCount = 1,
            PageNo = 1,
            PageSize = UserAdminService.PageSize,
        });
        web.Sp.Returns(UserAdminService.RoleListSp, new List<UserRoleDto> { new() { RoleId = "PARTNER_USER", RoleNm = "협력사 사용자", AssignedYn = "Y", EffectiveYn = "Y", AssignableYn = "Y" } });
        web.Sp.Returns(UserAdminService.ResetSp, new SaveResult(1, null));
        web.Sp.Returns(CompanyAdminService.ListSp, new PageResult<CompanyDto> { Items = [new() { CompanyCd = "P0001", CompanyNm = "협력사1" }], TotalCount = 1, PageNo = 1, PageSize = 500 });
        web.Sp.Returns("FW.USP_CODE_GET_LIST", new PageResult<CodeDto> { Items = [new() { Code = "ACTIVE", CodeNm = "사용" }], TotalCount = 1, PageNo = 1, PageSize = 500 });
        web.Sp.Returns("FW.USP_CODE_LANG_GET_LIST", new List<CodeAdminService.NameRow>());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> PageAsync(string token)
    {
        var response = await _web.NewClient(token).GetAsync("/Sys/User?_layout=tab", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private async Task<HttpResponseMessage> PostAsync(string token, string action, object body, string area = "Sys")
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(token), "/");
        var request = Ajax(HttpMethod.Post, $"/{area}/User/{action}", body);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await _web.NewClient(token, cookie).SendAsync(request, Ct);
    }

    private static object Save(params string[] roles) =>
        new { userId = "kim.ptn", rowVer = RowVer, userNm = "김협력", userStatCd = "ACTIVE", accountEndDt = "2030-12-31", roleIds = roles };

    [Fact]
    public async Task 화면_수정권한이있을때만_저장잠금해제초기화버튼()
    {
        var all = await PageAsync(_web.SignIn("usr.admin", AllFuncs));
        var reader = await PageAsync(_web.SignIn("usr.reader", [Home, $"{Program}:READ"]));

        foreach (var id in new[] { "user-save", "user-unlock", "user-mfa-reset" })
        {
            Assert.Contains($"id=\"{id}\"", all, StringComparison.Ordinal);
            Assert.DoesNotContain($"id=\"{id}\"", reader, StringComparison.Ordinal);
        }

        Assert.Contains("js/sys/user.js", all, StringComparison.Ordinal);
        Assert.Contains("data-me=\"usr.admin\"", all, StringComparison.Ordinal);
        Assert.Contains("<option value=\"P0001\">협력사1</option>", all, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 조회권한없음_화면목록역할모두403()
    {
        var token = _web.SignIn("usr.none", [Home]);

        Assert.Equal(HttpStatusCode.Forbidden, (await _web.NewClient(token).GetAsync("/Sys/User?_layout=tab", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Sys/User/List"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Sys/User/Roles?userId=kim.ptn"), Ct)).StatusCode);
    }

    [Fact]
    public async Task 저장과초기화_수정권한이없으면403_SP를부르지않는다()
    {
        var token = _web.SignIn("usr.reader2", [Home, $"{Program}:READ"]);
        var before = _web.Sp.Calls.Count(c => c.SpName is UserAdminService.SaveSp or UserAdminService.ResetSp);

        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(token, "Save", Save("PARTNER_USER"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(token, "Unlock", new { userId = "kim.ptn" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(token, "ResetMfa", new { userId = "kim.ptn" })).StatusCode);
        Assert.Equal(before, _web.Sp.Calls.Count(c => c.SpName is UserAdminService.SaveSp or UserAdminService.ResetSp));
    }

    [Fact]
    public async Task 저장_역할목록을정리해JSON으로넘긴다()
    {
        var token = _web.SignIn("usr.saver", AllFuncs);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(token, "Save", Save("PARTNER_USER", " PARTNER_ADMIN ", "PARTNER_USER"))).StatusCode);

        var p = _web.Sp.LastCall(UserAdminService.SaveSp).Parameters;
        Assert.Equal("""["PARTNER_USER","PARTNER_ADMIN"]""", p["P_ROLE_IDS"]);
        Assert.Equal(new DateOnly(2030, 12, 31), p["P_ACCOUNT_END_DT"]);
        Assert.Equal("kim.ptn", p["P_TARGET_USER_ID"]);
    }

    [Fact]
    public async Task 잠금해제와2단계인증초기화_각각의동작코드로SP를부른다()
    {
        var token = _web.SignIn("usr.resetter", AllFuncs);

        var unlock = await JsonAsync(await PostAsync(token, "Unlock", new { userId = "kim.ptn" }));
        Assert.Equal(UserResetActions.Unlock, _web.Sp.LastCall(UserAdminService.ResetSp).Parameters["P_ACTION_CD"]);
        await PostAsync(token, "ResetMfa", new { userId = "kim.ptn" });

        Assert.True(unlock.GetProperty("data").GetBoolean());
        Assert.Equal(UserResetActions.MfaReset, _web.Sp.LastCall(UserAdminService.ResetSp).Parameters["P_ACTION_CD"]);
    }

    [Fact]
    public async Task 목록응답에_이메일이나비밀키가없다()
    {
        var token = _web.SignIn("usr.list", AllFuncs);

        var body = await (await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Sys/User/List"), Ct)).Content.ReadAsStringAsync(Ct);

        Assert.Contains("\"userId\":\"kim.ptn\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("email", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task 협력사화면_회사유형검색이없고_SP에협력사프로그램ID를넘긴다()
    {
        var token = _web.SignIn("ptn.admin", [Home, "PTN_USER_MGT:READ", "PTN_USER_MGT:UPDATE"]);

        var response = await _web.NewClient(token).GetAsync("/Ptn/User?_layout=tab", Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);
        await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Ptn/User/List?companyCd=P0002"), Ct);
        var listProgram = _web.Sp.LastCall(UserAdminService.ListSp).Parameters["P_PROGRAM_ID"];
        await PostAsync(token, "Unlock", new { userId = "kim.ptn" }, "Ptn");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("id=\"user-company-filter\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"user-type-filter\"", html, StringComparison.Ordinal);
        Assert.Contains("data-url-list=\"/Ptn/User/List\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"user-save\"", html, StringComparison.Ordinal);
        Assert.Equal("PTN_USER_MGT", listProgram);
        Assert.Equal("PTN_USER_MGT", _web.Sp.LastCall(UserAdminService.ResetSp).Parameters["P_PROGRAM_ID"]);
    }

    [Fact]
    public async Task 협력사화면_본사사용자관리권한만으로는403_반대도403()
    {
        var sys = _web.SignIn("ptn.sysonly", AllFuncs);
        var ptn = _web.SignIn("ptn.only", [Home, "PTN_USER_MGT:READ"]);

        Assert.Equal(HttpStatusCode.Forbidden, (await _web.NewClient(sys).GetAsync("/Ptn/User?_layout=tab", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _web.NewClient(ptn).SendAsync(Ajax(HttpMethod.Get, "/Sys/User/List"), Ct)).StatusCode);
    }
}
