using Microsoft.AspNetCore.DataProtection;
using Portal.Core.Errors;
using Portal.Data;
using Portal.Security.Authentication;
using Portal.Security.Passwords;
using Portal.Security.Secrets;
using Portal.Testing;

namespace Portal.UnitTests.Security;

/// <summary>협력사 가입 신청: 입력 규칙, 비밀번호 규칙, 해시·암호문만 SP로 보낸다.</summary>
public sealed class PartnerJoinServiceTests
{
    private const string GoodPassword = "Partner#2026";
    private readonly FakeSpExecutor _sp = new();
    private readonly DataProtectionPersonalDataProtector _protector = new(new EphemeralDataProtectionProvider());

    public PartnerJoinServiceTests() => _sp.Returns(PartnerJoinService.SaveSp, new SaveResult(1, "42"));

    private PartnerJoinService Service => new(_sp, new FakePasswordHasher(), new PasswordPolicy(new FakePortalConfig()), _protector);

    private static PartnerJoinRequest Request(
        string bizRegNo = "123-45-67890", string userId = "kim.ptn", string email = "kim@partner.co.kr", string password = GoodPassword) =>
        new(bizRegNo, userId, " 김협력 ", email, password);

    private async Task<string> ErrorOf(PartnerJoinRequest request) =>
        (await Assert.ThrowsAsync<BizException>(() => Service.RequestAsync(request, TestContext.Current.CancellationToken))).MessageKey;

    [Fact]
    public async Task 신청_해시와암호화된이메일만보내고_신청번호를돌려준다()
    {
        var reqId = await Service.RequestAsync(Request(userId: " kim.ptn "), TestContext.Current.CancellationToken);

        Assert.Equal(42, reqId);
        var call = _sp.LastCall(PartnerJoinService.SaveSp);
        Assert.Equal(("123-45-67890", "kim.ptn", "김협력"), (call.Parameters["P_BIZ_REG_NO"], call.Parameters["P_REQ_USER_ID"], call.Parameters["P_USER_NM"]));
        Assert.Equal(FakePasswordHasher.Make(GoodPassword).Hash, call.Parameters["P_PWD_HASH"]);
        var email = Assert.IsType<byte[]>(call.Parameters["P_EMAIL_ENC"]);
        Assert.Equal("kim@partner.co.kr", _protector.Unprotect(email));
        Assert.DoesNotContain(call.Parameters.Values, v => v is string s && (s.Contains('@', StringComparison.Ordinal) || s == GoodPassword));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("123-45-6789a")]
    [InlineData("-1234567890")]
    public async Task 사업자등록번호형식오류(string bizRegNo) => Assert.Equal(PartnerJoinMessageKeys.BizRegNoFormat, await ErrorOf(Request(bizRegNo: bizRegNo)));

    [Theory]
    [InlineData("abc")]
    [InlineData(".kim")]
    [InlineData("김협력")]
    [InlineData("kim ptn")]
    [InlineData("abcdefghij_abcdefghij_abcdefghij")]
    public async Task 사용자ID형식오류(string userId) => Assert.Equal(PartnerJoinMessageKeys.UserIdFormat, await ErrorOf(Request(userId: userId)));

    [Theory]
    [InlineData("kim")]
    [InlineData("kim@")]
    [InlineData("kim @partner.co.kr")]
    [InlineData("kim@partner")]
    [InlineData("kim@-partner.com")]
    public async Task 이메일형식오류(string email) => Assert.Equal(PartnerJoinMessageKeys.EmailFormat, await ErrorOf(Request(email: email)));

    [Fact]
    public async Task 비밀번호규칙위반_첫번째규칙메시지() => Assert.Equal(PasswordMessageKeys.Weak, await ErrorOf(Request(password: "short")));

    [Fact]
    public async Task 비밀번호에ID포함_규칙위반() => Assert.Equal(PasswordMessageKeys.ContainsUserId, await ErrorOf(Request(password: "Kim.ptn#2026")));

    [Fact]
    public async Task 형식오류면_SP를부르지않는다()
    {
        await ErrorOf(Request(email: "x"));

        Assert.Empty(_sp.Calls);
    }
}

/// <summary>개인정보 암호화와 가림.</summary>
public sealed class PersonalDataProtectorTests
{
    [Fact]
    public void 암호화_복호화_왕복_평문이드러나지않는다()
    {
        var protector = new DataProtectionPersonalDataProtector(new EphemeralDataProtectionProvider());

        var data = protector.Protect("kim@partner.co.kr");

        Assert.DoesNotContain("partner", System.Text.Encoding.UTF8.GetString(data), StringComparison.Ordinal);
        Assert.Equal("kim@partner.co.kr", protector.Unprotect(data));
        Assert.NotEqual(data, protector.Protect("kim@partner.co.kr"));
    }

    [Fact]
    public void 설정비밀값보호기와_서로풀리지않는다()
    {
        var provider = new EphemeralDataProtectionProvider();
        var data = new DataProtectionPersonalDataProtector(provider).Protect("a");

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => provider.CreateProtector(DataProtectionSecretProtector.Purpose).Unprotect(data));
    }

    [Theory]
    [InlineData("kim@partner.co.kr", "ki***@partner.co.kr")]
    [InlineData("k@a.com", "k***@a.com")]
    [InlineData("@a.com", "***")]
    [InlineData("kim", "***")]
    [InlineData(null, "***")]
    public void 이메일가림(string? email, string masked) => Assert.Equal(masked, PersonalDataMask.Email(email));
}
