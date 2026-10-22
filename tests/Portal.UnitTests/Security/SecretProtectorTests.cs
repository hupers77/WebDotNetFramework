using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Portal.Core.Security;
using Portal.Security;
using Portal.Security.Secrets;

namespace Portal.UnitTests.Security;

/// <summary>설정 비밀값 암호화: 되돌릴 수 있고, 평문이 드러나지 않으며, 용도가 다른 키로는 풀리지 않는다.</summary>
public sealed class SecretProtectorTests
{
    private readonly DataProtectionSecretProtector _protector = new(new EphemeralDataProtectionProvider());

    [Fact]
    public void 암호화_복호화_왕복()
    {
        var protectedText = _protector.Protect("smtp-p@ss");

        Assert.DoesNotContain("smtp-p@ss", protectedText, StringComparison.Ordinal);
        Assert.Equal("smtp-p@ss", _protector.Unprotect(protectedText));
    }

    [Fact]
    public void 같은평문도_매번다른값() => Assert.NotEqual(_protector.Protect("a"), _protector.Protect("a"));

    [Fact]
    public void 다른용도의_보호기로는_풀리지않는다()
    {
        var provider = new EphemeralDataProtectionProvider();
        var protectedText = new DataProtectionSecretProtector(provider).Protect("a");

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => provider.CreateProtector("other").Unprotect(protectedText));
    }

    [Fact]
    public void AddPortalSecurity가_등록한다()
    {
        using var services = new ServiceCollection().AddLogging().AddPortalSecurity().BuildServiceProvider();

        var protector = services.GetRequiredService<ISecretProtector>();

        Assert.Equal("x", protector.Unprotect(protector.Protect("x")));
    }
}
