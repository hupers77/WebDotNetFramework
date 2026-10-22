using System.Net;
using Portal.Core.Results;
using Portal.Data;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>회사 관리 화면: 권한별 버튼(신규·등록 CREATE, 저장 UPDATE, 삭제 DELETE), 권한 없는 요청 차단, 등록·수정 구분, IP 형식 검사.</summary>
public sealed class CompanyScreenTests : IClassFixture<PortalWebFactory>
{
    private const string Program = "SYS_COMPANY_MGT";
    private const string Home = "DSH_HOME:READ";
    private static readonly string[] AllFuncs = [Home, $"{Program}:READ", $"{Program}:CREATE", $"{Program}:UPDATE", $"{Program}:DELETE"];
    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 9];
    private readonly PortalWebFactory _web;

    public CompanyScreenTests(PortalWebFactory web)
    {
        _web = web;
        web.Sp.Returns(CompanyAdminService.ListSp, new PageResult<CompanyDto>
        {
            Items = [new() { CompanyCd = "PTN01", CompanyNm = "협력사1", CompanyTypeCd = "PARTNER", CompanyTypeNm = "협력사", BizRegNo = "123-45-67890", RowVer = RowVer }],
            TotalCount = 1,
            PageNo = 1,
            PageSize = CompanyAdminService.PageSize,
        });
        web.Sp.Returns(CompanyAdminService.SaveSp, new SaveResult(1, "PTN01"));
        web.Sp.Returns("FW.USP_CODE_GET_LIST", new PageResult<CodeDto> { Items = [new() { Code = "PARTNER", CodeNm = "협력사" }], TotalCount = 1, PageNo = 1, PageSize = 500 });
        web.Sp.Returns("FW.USP_CODE_LANG_GET_LIST", new List<CodeAdminService.NameRow>());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> PageAsync(string token)
    {
        var response = await _web.NewClient(token).GetAsync("/Sys/Company?_layout=tab", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private async Task<HttpResponseMessage> PostAsync(string token, string action, object body)
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(token), "/");
        var request = Ajax(HttpMethod.Post, $"/Sys/Company/{action}", body);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await _web.NewClient(token, cookie).SendAsync(request, Ct);
    }

    private static object Body(byte[]? rowVer, string? ip = null, string? code = "PTN01") =>
        new { companyCd = code, rowVer, companyNm = "협력사1", companyTypeCd = "PARTNER", bizRegNo = "123-45-67890", allowIpList = ip, contractEndDt = "2030-12-31", sortNo = 1, useYn = "Y" };

    [Fact]
    public async Task 화면_권한별로_신규저장삭제버튼()
    {
        var all = await PageAsync(_web.SignIn("co.admin", AllFuncs));
        var reader = await PageAsync(_web.SignIn("co.reader", [Home, $"{Program}:READ"]));

        foreach (var id in new[] { "company-new", "company-create", "company-save", "company-delete" })
        {
            Assert.Contains($"id=\"{id}\"", all, StringComparison.Ordinal);
            Assert.DoesNotContain($"id=\"{id}\"", reader, StringComparison.Ordinal);
        }

        Assert.Contains("js/sys/company.js", all, StringComparison.Ordinal);
        Assert.Contains("data-partner-nm=\"협력사\"", all, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 조회권한없음_화면과목록403()
    {
        var token = _web.SignIn("co.none", [Home]);

        Assert.Equal(HttpStatusCode.Forbidden, (await _web.NewClient(token).GetAsync("/Sys/Company?_layout=tab", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Sys/Company/List"), Ct)).StatusCode);
    }

    [Fact]
    public async Task 등록수정삭제_기능권한이없으면403_SP를부르지않는다()
    {
        var token = _web.SignIn("co.reader2", [Home, $"{Program}:READ"]);
        var before = _web.Sp.Calls.Count(c => c.SpName is CompanyAdminService.SaveSp or CompanyAdminService.DeleteSp);

        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(token, "Create", Body(null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(token, "Save", Body(RowVer))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(token, "Delete", new { companyCd = "PTN01", rowVer = RowVer })).StatusCode);
        Assert.Equal(before, _web.Sp.Calls.Count(c => c.SpName is CompanyAdminService.SaveSp or CompanyAdminService.DeleteSp));
    }

    [Fact]
    public async Task 수정권한만있으면_등록은403()
    {
        var token = _web.SignIn("co.updater", [Home, $"{Program}:READ", $"{Program}:UPDATE"]);

        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(token, "Create", Body(null))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(token, "Save", Body(RowVer))).StatusCode);
    }

    [Fact]
    public async Task 등록은RowVer없이만_수정은RowVer가있어야()
    {
        var token = _web.SignIn("co.mixed", AllFuncs);

        Assert.Equal("MSG.INVALID_INPUT", (await JsonAsync(await PostAsync(token, "Create", Body(RowVer)))).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("MSG.INVALID_INPUT", (await JsonAsync(await PostAsync(token, "Save", Body(null)))).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("MSG.INVALID_INPUT", (await JsonAsync(await PostAsync(token, "Save", Body(RowVer, code: null)))).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task 저장_IP목록과날짜를정리해SP에넘기고_기한바꾼계정수를돌려준다()
    {
        var token = _web.SignIn("co.saver", AllFuncs);

        var json = await JsonAsync(await PostAsync(token, "Save", Body(RowVer, ip: "203.0.113.0/24\n203.0.113.7")));

        Assert.Equal(0, json.GetProperty("data").GetProperty("movedUserCnt").GetInt32());
        var p = _web.Sp.LastCall(CompanyAdminService.SaveSp).Parameters;
        Assert.Equal("203.0.113.0/24, 203.0.113.7", p["P_ALLOW_IP_LIST"]);
        Assert.Equal(new DateOnly(2030, 12, 31), p["P_CONTRACT_END_DT"]);
        Assert.Equal(RowVer, p["P_ROW_VER"]);
    }

    [Fact]
    public async Task 저장_틀린IP는_메시지키로거부()
    {
        var token = _web.SignIn("co.bad", AllFuncs);

        var ip = (await JsonAsync(await PostAsync(token, "Save", Body(RowVer, ip: "10.0.0")))).GetProperty("error");

        Assert.Equal(CompanyMessageKeys.IpInvalid, ip.GetProperty("code").GetString());
    }

    [Fact]
    public async Task 등록_보낸코드는무시하고_SP가부여한코드를돌려준다()
    {
        var json = await JsonAsync(await PostAsync(_web.SignIn("co.creator", AllFuncs), "Create", Body(null, code: "HQ")));

        Assert.Null(_web.Sp.LastCall(CompanyAdminService.SaveSp).Parameters["P_COMPANY_CD"]);
        Assert.Equal("PTN01", json.GetProperty("data").GetProperty("companyCd").GetString());
    }
}
