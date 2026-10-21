using Portal.Security.Passwords;

namespace Portal.UnitTests.Security;

public sealed class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_Verify_같은비밀번호_성공_다른비밀번호_실패()
    {
        var hash = _hasher.Hash("Portal!2026");

        Assert.Equal(64, hash.Hash.Length);
        Assert.Equal(16, hash.Salt.Length);
        Assert.True(_hasher.Verify("Portal!2026", hash.Hash, hash.Salt));
        Assert.False(_hasher.Verify("portal!2026", hash.Hash, hash.Salt));
    }

    [Fact]
    public void Hash_같은비밀번호도_salt가달라_해시가다르다()
    {
        var a = _hasher.Hash("Portal!2026");
        var b = _hasher.Hash("Portal!2026");

        Assert.NotEqual(a.Salt, b.Salt);
        Assert.NotEqual(a.Hash, b.Hash);
    }

    [Fact]
    public void Verify_저장값길이가틀리면_계산없이실패()
    {
        var hash = _hasher.Hash("Portal!2026");

        Assert.False(_hasher.Verify("Portal!2026", hash.Hash.AsSpan(0, 32), hash.Salt));
        Assert.False(_hasher.Verify("Portal!2026", hash.Hash, []));
    }

    [Fact]
    public void 반복횟수_보안기준값고정() =>
        Assert.Equal(210_000, Pbkdf2PasswordHasher.Iterations);
}
