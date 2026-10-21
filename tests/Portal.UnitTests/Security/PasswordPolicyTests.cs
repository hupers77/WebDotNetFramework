using Portal.Security.Passwords;
using Portal.Testing;

namespace Portal.UnitTests.Security;

public sealed class PasswordPolicyTests
{
    private readonly PasswordPolicy _policy = new(new FakePortalConfig());

    [Theory]
    [InlineData("Abcdef1!")]      // 4종류 8자
    [InlineData("abcdef1!")]      // 3종류 8자
    [InlineData("abcdefgh12")]    // 2종류 10자
    [InlineData("가나다라마바ab1")] // 한글은 기타 문자로 센다: 기타·소문자·숫자 3종류 9자
    public void Validate_규칙충족_통과(string password) =>
        Assert.Empty(_policy.Validate(password, "hong"));

    [Theory]
    [InlineData("abcde1!")]       // 3종류 7자
    [InlineData("abcdefg12")]     // 2종류 9자
    [InlineData("abcdefghijklmn")] // 1종류
    public void Validate_길이종류미달_약함_인자로최소길이(string password)
    {
        var v = Assert.Single(_policy.Validate(password, "hong"));

        Assert.Equal(PasswordMessageKeys.Weak, v.MessageKey);
        Assert.Equal([8, 10], v.Args);
    }

    [Fact]
    public void Validate_사용자ID포함_대소문자무시()
    {
        var v = Assert.Single(_policy.Validate("xHONGx12!a", "hong"));

        Assert.Equal(PasswordMessageKeys.ContainsUserId, v.MessageKey);
    }

    [Fact]
    public void Validate_같은문자4번연속_금지_3번은허용()
    {
        Assert.Contains(_policy.Validate("abc1111!x", "hong"), v => v.MessageKey == PasswordMessageKeys.Repeated);
        Assert.Empty(_policy.Validate("abc111!xy", "hong"));
    }

    [Fact]
    public void Validate_최대길이초과_다른검사없이길이오류만()
    {
        var v = Assert.Single(_policy.Validate(new string('a', 129), "hong"));

        Assert.Equal(PasswordMessageKeys.TooLong, v.MessageKey);
    }

    [Fact]
    public void Validate_설정값을따른다()
    {
        var policy = new PasswordPolicy(new FakePortalConfig().Set("PWD.MIN_LEN_3TYPES", "12"));

        Assert.Contains(policy.Validate("Abcdef1!", "hong"), v => v.MessageKey == PasswordMessageKeys.Weak);
    }

    [Fact]
    public void Validate_여러규칙위반_모두반환()
    {
        var keys = _policy.Validate("hongggg", "hong").Select(v => v.MessageKey);

        Assert.Equal([PasswordMessageKeys.Weak, PasswordMessageKeys.ContainsUserId, PasswordMessageKeys.Repeated], keys);
    }
}
