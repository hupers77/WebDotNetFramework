using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Portal.Core.Results;
using Portal.Data;
using Portal.Security.Authentication;
using Portal.Security.Secrets;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>협력사 가입 신청 화면(로그인 전)과 협력사 계정 승인 화면: 입력 오류 표시, 암호문만 저장, 권한별 버튼, 목록의 이메일 가림.</summary>
public sealed class PartnerJoinScreenTests : IClassFixture<PortalWebFactory>
{
    private const string Program = "SYS_PARTNER_APPROVE";
    private const string Home = "DSH_HOME:READ";
    private readonly PortalWebFactory _web;

    public PartnerJoinScreenTests(PortalWebFactory web)
    {
        _web = web;
        web.Sp.Returns(PartnerJoinService.SaveSp, new SaveResult(1, "7"));
        web.Sp.Returns("FW.USP_CODE_GET_LIST", new PageResult<CodeDto> { Items = [new() { Code = "REQUESTED", CodeNm = "처리 대기" }], TotalCount = 1, PageNo = 1, PageSize = 500 });
        web.Sp.Returns("FW.USP_CODE_LANG_GET_LIST", new List<CodeAdminService.NameRow>());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> PostJoinAsync(string email = "kim@partner.example.com", string confirm = "Partner#2026")
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(), "/Account/Join");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Join")
        {
            Content = new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", csrf),
                new("BizRegNo", "999-88-77777"),
                new("UserId", "kim.ptn"),
                new("UserNm", "김협력"),
                new("Email", email),
                new("Password", "Partner#2026"),
                new("PasswordConfirm", confirm),
            ]),
        };
        var response = await _web.NewClient(otherCookie: cookie).SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    [Fact]
    public async Task 로그인화면에_가입신청링크_가입화면은비로그인으로열림()
    {
        Assert.Contains("href=\"/Account/Join\"", await _web.NewClient().GetStringAsync("/Account/Login", Ct), StringComparison.Ordinal);

        var html = await _web.NewClient().GetStringAsync("/Account/Join", Ct);

        Assert.Contains("autocomplete=\"new-password\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"BizRegNo\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 신청성공_완료안내_이메일은암호문으로보낸다()
    {
        var html = await PostJoinAsync();

        Assert.Contains("MSG.JOIN_REQUESTED", html, StringComparison.Ordinal);
        var call = _web.Sp.LastCall(PartnerJoinService.SaveSp);
        var email = Assert.IsType<byte[]>(call.Parameters["P_EMAIL_ENC"]);
        Assert.Equal("kim@partner.example.com", _web.Services.GetRequiredService<IPersonalDataProtector>().Unprotect(email));
    }

    [Fact]
    public async Task 입력오류_메시지와입력값다시표시_비밀번호는비운다()
    {
        var html = await PostJoinAsync(email: "not-an-email");

        Assert.Contains("role=\"alert\"", html, StringComparison.Ordinal);
        Assert.Contains("VAL.EMAIL_FORMAT", html, StringComparison.Ordinal);
        Assert.Contains("value=\"kim.ptn\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Partner#2026", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 비밀번호확인불일치_SP를부르지않는다()
    {
        var before = _web.Sp.Calls.Count(c => c.SpName == PartnerJoinService.SaveSp);

        var html = await PostJoinAsync(confirm: "Other#2026");

        Assert.Contains("VAL.PWD_CONFIRM_MISMATCH", html, StringComparison.Ordinal);
        Assert.Equal(before, _web.Sp.Calls.Count(c => c.SpName == PartnerJoinService.SaveSp));
    }

    [Fact]
    public async Task 승인화면_승인권한이면처리버튼_목록은이메일을가리고암호문을보내지않는다()
    {
        var protector = _web.Services.GetRequiredService<IPersonalDataProtector>();
        _web.Sp.Returns(PartnerApproveService.ListSp, new PageResult<PartnerJoinDto>
        {
            Items = [new() { ReqId = 1, UserId = "kim.ptn", EmailEnc = protector.Protect("kim@partner.example.com"), ReqStatCd = "REQUESTED", RowVer = new byte[8] }],
            TotalCount = 1,
            PageNo = 1,
            PageSize = PartnerApproveService.MaxRows,
        });
        var approver = _web.SignIn("join.approver", [Home, $"{Program}:READ", $"{Program}:APPROVE"]);
        var reader = _web.SignIn("join.reader", [Home, $"{Program}:READ"]);

        Assert.Contains("id=\"join-approve\"", await _web.NewClient(approver).GetStringAsync("/Sys/PartnerApprove?_layout=tab", Ct), StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"join-approve\"", await _web.NewClient(reader).GetStringAsync("/Sys/PartnerApprove?_layout=tab", Ct), StringComparison.Ordinal);

        var response = await _web.NewClient(reader).SendAsync(Ajax(HttpMethod.Get, "/Sys/PartnerApprove/List?reqStatCd=REQUESTED"), Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("\"emailMasked\":\"ki***@partner.example.com\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("emailEnc", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kim@partner", body, StringComparison.Ordinal);
    }
}
