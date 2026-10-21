using Portal.Security.Authentication;

namespace Portal.UnitTests.Security;

public sealed class SessionTokenTests
{
    [Fact]
    public void Create_토큰은URL안전43자_해시는소문자16진수64자()
    {
        var (token, hash) = SessionToken.Create();

        Assert.True(SessionToken.IsWellFormed(token));
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.Equal(hash, SessionToken.Hash(token));
    }

    [Fact]
    public void Create_매번다른값()
    {
        var a = SessionToken.Create();
        var b = SessionToken.Create();

        Assert.NotEqual(a.Token, b.Token);
        Assert.NotEqual(a.Hash, b.Hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("' OR 1=1 --aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void IsWellFormed_형식이다르면거부(string? token) =>
        Assert.False(SessionToken.IsWellFormed(token));
}
