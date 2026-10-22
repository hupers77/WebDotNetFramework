using System.Net;
using Portal.Core.Results;
using Portal.Data;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>사용자별 예외 권한 화면: 저장 버튼은 UPDATE만, 사용자 목록은 이 화면의 데이터 범위, 프로그램별 묶음, 범위 밖 대상, 저장 값, 입력 검사, 권한 없는 요청 차단.</summary>
public sealed class UserAuthScreenTests : IClassFixture<PortalWebFactory>
{
    private const string Program = "SYS_USER_AUTH_MGT";
    private const string Home = "DSH_HOME:READ";
    private static readonly string[] AllFuncs = [Home, $"{Program}:READ", $"{Program}:UPDATE"];
    private static readonly byte[] Version = [.. Enumerable.Range(1, 32).Select(i => (byte)i)];
    private readonly PortalWebFactory _web;

    public UserAuthScreenTests(PortalWebFactory web)
    {
        _web = web;
        web.Sp.Returns("FW.USP_CODE_GET_LIST", new PageResult<CodeDto>
        {
            Items = [new() { Code = "READ", CodeNm = "조회", UseYn = "Y" }, new() { Code = "EXPORT", CodeNm = "내보내기", UseYn = "Y" }],
            TotalCount = 2,
            PageNo = 1,
            PageSize = 500,
        });
        web.Sp.Returns("FW.USP_CODE_LANG_GET_LIST", new List<CodeAdminService.NameRow>());
        web.Sp.Returns(CompanyAdminService.ListSp, new PageResult<CompanyDto> { Items = [new() { CompanyCd = "P0001", CompanyNm = "협력사1" }], TotalCount = 1, PageNo = 1, PageSize = 500 });
        web.Sp.Returns(UserAdminService.ListSp, new PageResult<UserAdminDto>
        {
            Items = [new() { UserId = "kim.emp", UserNm = "김본사", CompanyCd = "HQ", CompanyNm = "본사", UserTypeCd = "EMP", UserStatCd = "ACTIVE" }],
            TotalCount = 1,
            PageNo = 1,
            PageSize = UserAdminService.PageSize,
        });
        web.Sp.Returns(UserAuthService.ListSp, p => (string?)p["P_TARGET_USER_ID"] == "ghost"
            ? new object?[] { null, null }
            : new object[]
            {
                new UserAuthTarget { UserId = "kim.emp", UserNm = "김본사", CompanyNm = "본사", CompanyTypeCd = "HQ", UserStatCd = "ACTIVE", Version = Version },
                new List<UserAuthRow>
                {
                    new() { ProgramId = "IT_NO_MENU", ProgramNm = "메뉴 없는 화면", OpenScopeCd = "EMP", GrantableYn = "Y", FuncCd = "READ", RoleYn = "Y" },
                    new() { ProgramId = "IT_NO_MENU", ProgramNm = "메뉴 없는 화면", OpenScopeCd = "EMP", GrantableYn = "Y", FuncCd = "EXPORT", RoleYn = "N", GrantType = "GRANT", EndDt = new DateTime(2026, 12, 31) },
                },
            });
        web.Sp.Returns(UserAuthService.SaveSp, new SaveResult(1, null));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> PageAsync(string token)
    {
        var response = await _web.NewClient(token).GetAsync("/Sys/UserAuth?_layout=tab", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private async Task<HttpResponseMessage> PostAsync(string token, object body)
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(token), "/");
        var request = Ajax(HttpMethod.Post, "/Sys/UserAuth/Save", body);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await _web.NewClient(token, cookie).SendAsync(request, Ct);
    }

    [Fact]
    public async Task 화면_회사검색과_저장버튼은수정권한만()
    {
        var all = await PageAsync(_web.SignIn("ua.admin", AllFuncs));
        var reader = await PageAsync(_web.SignIn("ua.reader", [Home, $"{Program}:READ"]));

        Assert.Contains("<option value=\"P0001\">협력사1</option>", all, StringComparison.Ordinal);
        Assert.Contains("id=\"user-auth-save\"", all, StringComparison.Ordinal);
        Assert.Contains("js/sys/user-auth.js", all, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"user-auth-save\"", reader, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 사용자목록은_이화면의데이터범위로찾는다()
    {
        var token = _web.SignIn("ua.users", AllFuncs);

        var json = await JsonAsync(await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Sys/UserAuth/Users?keyword=kim&companyCd=HQ"), Ct));

        Assert.Equal("kim.emp", json.GetProperty("data").GetProperty("items")[0].GetProperty("userId").GetString());
        var p = _web.Sp.LastCall(UserAdminService.ListSp).Parameters;
        Assert.Equal((Program, "kim", "HQ"), ((string)p["P_PROGRAM_ID"]!, (string)p["P_KEYWORD"]!, (string)p["P_COMPANY_CD"]!));
    }

    [Fact]
    public async Task 예외목록_프로그램별로묶고_종료일은날짜만()
    {
        var token = _web.SignIn("ua.list", AllFuncs);

        var json = await JsonAsync(await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Sys/UserAuth/List?userId=kim.emp"), Ct));

        var data = json.GetProperty("data");
        Assert.Equal("kim.emp", data.GetProperty("target").GetProperty("userId").GetString());
        var program = data.GetProperty("programs")[0];
        Assert.Equal(("IT_NO_MENU", "메뉴 없는 화면", true), (program.GetProperty("programId").GetString(), program.GetProperty("programNm").GetString(), program.GetProperty("grantable").GetBoolean()));
        var export = program.GetProperty("funcs")[1];
        Assert.Equal(("EXPORT", false, "GRANT", "2026-12-31"), (export.GetProperty("funcCd").GetString(), export.GetProperty("byRole").GetBoolean(), export.GetProperty("grantType").GetString(), export.GetProperty("endDt").GetString()));
        Assert.Equal("kim.emp", _web.Sp.LastCall(UserAuthService.ListSp).Parameters["P_TARGET_USER_ID"]);
    }

    [Fact]
    public async Task 범위밖대상은_없는것으로()
    {
        var token = _web.SignIn("ua.ghost", AllFuncs);

        var json = await JsonAsync(await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Sys/UserAuth/List?userId=ghost"), Ct));

        Assert.Equal("MSG.NOT_FOUND", json.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task 저장_목록을JSON으로_수정권한이없으면403()
    {
        var body = new { targetUserId = "kim.emp", version = Version, rows = new[] { new { programId = " SYS_CODE_MGT ", funcCd = "EXPORT", grantType = "DENY", endDt = "2026-12-31" } } };

        var ok = await PostAsync(_web.SignIn("ua.save", AllFuncs), body);

        Assert.Equal(1, (await JsonAsync(ok)).GetProperty("data").GetInt32());
        var p = _web.Sp.LastCall(UserAuthService.SaveSp).Parameters;
        Assert.Equal(("kim.emp", """[{"programId":"SYS_CODE_MGT","funcCd":"EXPORT","grantType":"DENY","endDt":"2026-12-31"}]"""), ((string)p["P_TARGET_USER_ID"]!, (string)p["P_ROWS"]!));
        Assert.Equal(Version, (byte[])p["P_VERSION"]!);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(_web.SignIn("ua.reader2", [Home, $"{Program}:READ"]), body)).StatusCode);
    }

    [Theory]
    [InlineData("ALLOW", "2026-12-31", 32)] // 없는 유형
    [InlineData("GRANT", null, 32)] // 종료일 없음
    [InlineData("GRANT", "2026-12-31", 8)] // 버전 길이
    public async Task 저장_잘못된입력은_거부(string grantType, string? endDt, int versionLength)
    {
        var body = new { targetUserId = "kim.emp", version = new byte[versionLength], rows = new[] { new { programId = "SYS_CODE_MGT", funcCd = "EXPORT", grantType, endDt } } };

        var json = await JsonAsync(await PostAsync(_web.SignIn("ua.bad", AllFuncs), body));

        Assert.Equal("MSG.INVALID_INPUT", json.GetProperty("error").GetProperty("code").GetString());
    }
}
