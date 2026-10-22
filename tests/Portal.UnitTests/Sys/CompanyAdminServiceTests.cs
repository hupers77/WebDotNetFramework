using Portal.Core.Errors;
using Portal.Testing;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.UnitTests.Sys;

/// <summary>회사 관리 Service: 허용 IP 형식 검사·정리, 자기 회사 IP 차단 방지, 사업자등록번호 형식, 계정 기한 변경 건수.</summary>
public sealed class CompanyAdminServiceTests
{
    private readonly FakeSpExecutor _sp = new();
    private readonly FakeCurrentUser _user = new() { CompanyCd = "HQ", ClientIp = "10.1.2.3" };

    private CompanyAdminService Service => new(_sp, _user);

    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 1];

    private static CompanySaveRequest Request(string code = "PTN01", string? ip = null, string? biz = "123-45-67890", bool isNew = false) =>
        new() { CompanyCd = code, RowVer = isNew ? null : RowVer, CompanyNm = "  협력사  ", CompanyTypeCd = CompanyTypes.Partner, BizRegNo = biz, AllowIpList = ip, UseYn = "Y" };

    [Theory]
    [InlineData(null, null)]
    [InlineData("  \n ", null)]
    [InlineData("203.0.113.0/24\n192.168.1.10 ; 203.0.113.0/24,2001:db8::/32", "203.0.113.0/24, 192.168.1.10, 2001:db8::/32")]
    public void IP목록_줄바꿈쉼표를맞추고_중복을뺀다(string? input, string? expected) =>
        Assert.Equal(expected, CompanyAdminService.NormalizeIpList(input));

    [Theory]
    [InlineData("10.0.0")]
    [InlineData("10.0.0.0/33")]
    [InlineData("intranet")]
    public void IP목록_틀린항목은_그항목을담아오류(string bad)
    {
        var ex = Assert.Throws<BizException>(() => CompanyAdminService.NormalizeIpList($"10.0.0.0/8, {bad}"));

        Assert.Equal(CompanyMessageKeys.IpInvalid, ex.MessageKey);
        Assert.Equal(bad, ex.Args[0]);
    }

    [Fact]
    public async Task 저장_정리한값으로SP를부르고_기한바꾼계정수를돌려준다()
    {
        _sp.Returns(CompanyAdminService.SaveSp, new Portal.Data.SaveResult(3, "PTN01"));

        var result = await Service.SaveAsync(Request(ip: "203.0.113.0/24\n203.0.113.9"));

        var p = _sp.LastCall(CompanyAdminService.SaveSp).Parameters;
        Assert.Equal(("협력사", "123-45-67890", "203.0.113.0/24, 203.0.113.9"), ((string)p["P_COMPANY_NM"]!, (string)p["P_BIZ_REG_NO"]!, (string)p["P_ALLOW_IP_LIST"]!));
        Assert.Equal(new CompanySaveResult("PTN01", 2), result);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("ABC-12-34567")]
    public async Task 저장_사업자등록번호형식이틀리면_SP를부르지않는다(string biz)
    {
        var ex = await Assert.ThrowsAsync<BizException>(() => Service.SaveAsync(Request(biz: biz)));

        Assert.Equal("VAL.BIZ_REG_NO_FORMAT", ex.MessageKey);
        Assert.Empty(_sp.Calls);
    }

    [Fact]
    public async Task 저장_자기회사IP에서지금주소가빠지면_거부한다()
    {
        var ex = await Assert.ThrowsAsync<BizException>(() => Service.SaveAsync(Request(code: "HQ", ip: "192.168.0.0/16")));

        Assert.Equal(CompanyMessageKeys.IpSelfLockout, ex.MessageKey);
        Assert.Equal("10.1.2.3", ex.Args[0]);
        Assert.Empty(_sp.Calls);
    }

    [Fact]
    public async Task 등록_코드는넘기지않는다()
    {
        await Service.SaveAsync(Request(code: "HQ", isNew: true));

        Assert.Null(_sp.LastCall(CompanyAdminService.SaveSp).Parameters["P_COMPANY_CD"]);
    }

    [Fact]
    public async Task 저장_자기회사라도지금주소가포함되면_저장하고_다른회사는검사하지않는다()
    {
        await Service.SaveAsync(Request(code: "HQ", ip: "10.0.0.0/8"));
        await Service.SaveAsync(Request(code: "PTN02", ip: "192.168.0.0/16"));

        Assert.Equal(2, _sp.Calls.Count);
    }
}
