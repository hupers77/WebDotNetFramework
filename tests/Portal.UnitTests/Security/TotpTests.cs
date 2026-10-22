using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Portal.Security.Mfa;

namespace Portal.UnitTests.Security;

public sealed class TotpTests
{
    // RFC 6238 부록 B의 SHA1 시험 키
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    private static DateTimeOffset At(long unixSeconds) => DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

    [Theory]
    [InlineData(59, "287082")]
    [InlineData(1111111109, "081804")]
    [InlineData(1111111111, "050471")]
    [InlineData(1234567890, "005924")]
    [InlineData(2000000000, "279037")]
    public void RFC6238_시험값(long unixSeconds, string expected) =>
        Assert.Equal(expected, Totp.Code(RfcSecret, Totp.StepOf(At(unixSeconds))));

    [Fact]
    public void 앞뒤한단계까지_받고_맞은단계를돌려준다()
    {
        var now = At(1111111109);
        var step = Totp.StepOf(now);

        Assert.Equal(step, Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step), now, null));
        Assert.Equal(step - 1, Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step - 1), now, null));
        Assert.Equal(step + 1, Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step + 1), now, null));
        Assert.Null(Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step - 2), now, null));
        Assert.Null(Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step + 2), now, null));
    }

    [Fact]
    public void 이미쓴단계이하는_재사용으로거부()
    {
        var now = At(1111111109);
        var step = Totp.StepOf(now);
        var code = Totp.Code(RfcSecret, step);

        Assert.Null(Totp.Verify(RfcSecret, code, now, step));
        Assert.Null(Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step - 1), now, step - 1));
        Assert.Equal(step, Totp.Verify(RfcSecret, code, now, step - 1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("08180a")]
    [InlineData("０８１８０４")] // 전각 숫자
    public void 형식이틀리면_거부(string? code) =>
        Assert.Null(Totp.Verify(RfcSecret, code, At(1111111109), null));

    [Fact]
    public void 가운데공백은_무시()
    {
        var now = At(1111111109);
        Assert.NotNull(Totp.Verify(RfcSecret, "081 804", now, null));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Base32_RFC4648_패딩없음(string input, string expected) =>
        Assert.Equal(expected, Totp.ToBase32(Encoding.ASCII.GetBytes(input)));

    [Fact]
    public void 등록링크_인증앱형식()
    {
        var uri = Totp.KeyUri("포털 Portal", "hong", RfcSecret);

        Assert.StartsWith("otpauth://totp/", uri, StringComparison.Ordinal);
        Assert.Contains("secret=" + Totp.ToBase32(RfcSecret), uri, StringComparison.Ordinal);
        Assert.Contains("algorithm=SHA1&digits=6&period=30", uri, StringComparison.Ordinal);
        Assert.DoesNotContain(" ", uri, StringComparison.Ordinal);
        Assert.Contains("hong", uri, StringComparison.Ordinal);
    }

    [Fact]
    public void 새비밀키_160비트_매번다르다()
    {
        var a = Totp.NewSecret();
        Assert.Equal(Totp.SecretSize, a.Length);
        Assert.NotEqual(a, Totp.NewSecret());
    }

    [Fact]
    public void 대기표_암호화왕복_위조와다른용도는null()
    {
        var provider = new EphemeralDataProtectionProvider();
        var tickets = new MfaTicketProtector(provider);
        var secret = Totp.NewSecret();

        var withSecret = tickets.Unprotect(tickets.Protect(new MfaTicket("hong", secret)));
        var enrolled = tickets.Unprotect(tickets.Protect(new MfaTicket("kim", null)));

        Assert.Equal("hong", withSecret!.UserId);
        Assert.Equal(secret, withSecret.PendingSecret);
        Assert.Equal("kim", enrolled!.UserId);
        Assert.Null(enrolled.PendingSecret);
        Assert.Null(tickets.Unprotect(null));
        Assert.Null(tickets.Unprotect("garbage"));
        Assert.Null(tickets.Unprotect(tickets.Protect(new MfaTicket("hong", null)) + "x"));
        // 다른 용도로 암호화한 값(예: 세션 비밀값)은 대기표로 받지 않는다
        Assert.Null(tickets.Unprotect(provider.CreateProtector("other").Protect("hong\n")));
    }

    [Fact]
    public void 비밀키암호화_왕복()
    {
        var protector = new DataProtectionMfaSecretProtector(new EphemeralDataProtectionProvider());
        var secret = Totp.NewSecret();

        var stored = protector.Protect(secret);

        Assert.NotEqual(secret, stored);
        Assert.Equal(secret, protector.Unprotect(stored));
    }
}
