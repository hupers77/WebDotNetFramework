using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Portal.Testing;
using Portal.Web.Infrastructure;

namespace Portal.UnitTests.Web;

public sealed class LoginCryptoTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero));

    private string Encrypt(LoginCrypto crypto, string userId, string password, DateTimeOffset at)
    {
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(crypto.PublicKey), out _);
        var json = JsonSerializer.Serialize(new { u = userId, p = password, t = at.ToUnixTimeMilliseconds() });
        return Convert.ToBase64String(rsa.Encrypt(Encoding.UTF8.GetBytes(json), RSAEncryptionPadding.OaepSHA256));
    }

    [Fact]
    public void 브라우저와_같은_방식으로_암호화한_값을_푼다_평문이_보이지_않는다()
    {
        using var crypto = new LoginCrypto(_time);
        var enc = Encrypt(crypto, "iv0106533", "p@ss", _time.GetUtcNow());

        Assert.DoesNotContain("p@ss", enc);
        var plain = crypto.TryDecrypt(enc);
        Assert.NotNull(plain);
        Assert.Equal("iv0106533", plain.Value.UserId);
        Assert.Equal("p@ss", plain.Value.Password);
    }

    [Fact]
    public void 오래된_암호문_깨진_값은_받지_않는다()
    {
        using var crypto = new LoginCrypto(_time);
        var old = Encrypt(crypto, "u", "p", _time.GetUtcNow().AddMinutes(-6));

        Assert.Null(crypto.TryDecrypt(old));
        Assert.Null(crypto.TryDecrypt("not-base64!!"));
        Assert.Null(crypto.TryDecrypt(null));
        Assert.Null(crypto.TryDecrypt(Convert.ToBase64String(new byte[256])));
    }

    [Fact]
    public void 다른_키로_암호화한_값은_받지_않는다()
    {
        using var a = new LoginCrypto(_time);
        using var b = new LoginCrypto(_time);

        Assert.Null(b.TryDecrypt(Encrypt(a, "u", "p", _time.GetUtcNow())));
    }
}
