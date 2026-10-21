using Portal.Core.Errors;
using Portal.Security.Authentication;
using Portal.Security.Passwords;
using Portal.Testing;

namespace Portal.UnitTests.Security;

public sealed class PasswordServiceTests
{
    private readonly FakeSpExecutor _sp = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly FakePortalConfig _config = new();

    private PasswordService Service => new(_sp, _hasher, new PasswordPolicy(_config), _config, new FakeCurrentUser { UserId = "hong" });

    public PasswordServiceTests() =>
        _sp.Returns(AuthSp.PasswordHistory, new[]
        {
            Row(0, "Current!2026"),
            Row(2, "Older!2026a"),
            Row(1, "Oldest!2026"),
        });

    private static PasswordHistoryRow Row(int seq, string pwd)
    {
        var h = FakePasswordHasher.Make(pwd);
        return new PasswordHistoryRow { SeqNo = seq, PwdHash = h.Hash, PwdSalt = h.Salt };
    }

    [Fact]
    public async Task 정상변경_새해시저장()
    {
        await Service.ChangeAsync("Current!2026", "Brand#New2026");

        var call = _sp.LastCall(AuthSp.PasswordChange);
        Assert.Equal(FakePasswordHasher.Make("Brand#New2026").Hash, call.Parameters["P_PWD_HASH"]);
    }

    [Fact]
    public async Task 현재비밀번호불일치()
    {
        var ex = await Assert.ThrowsAsync<BizException>(() => Service.ChangeAsync("wrong", "Brand#New2026"));

        Assert.Equal(PasswordMessageKeys.CurrentMismatch, ex.MessageKey);
    }

    [Fact]
    public async Task 규칙위반_첫위반을인자와함께()
    {
        var ex = await Assert.ThrowsAsync<BizException>(() => Service.ChangeAsync("Current!2026", "short1"));

        Assert.Equal(PasswordMessageKeys.Weak, ex.MessageKey);
        Assert.Equal([8, 10], ex.Args);
    }

    [Theory]
    [InlineData("Current!2026")]
    [InlineData("Oldest!2026")]
    public async Task 현재와최근비밀번호_재사용금지(string reused)
    {
        var ex = await Assert.ThrowsAsync<BizException>(() => Service.ChangeAsync("Current!2026", reused));

        Assert.Equal(PasswordMessageKeys.Reused, ex.MessageKey);
        Assert.DoesNotContain(_sp.Calls, c => c.SpName == AuthSp.PasswordChange);
    }

    [Fact]
    public async Task 로그인전_권한없음()
    {
        var service = new PasswordService(_sp, _hasher, new PasswordPolicy(_config), _config, FakeCurrentUser.Anonymous());

        await Assert.ThrowsAsync<BizException>(() => service.ChangeAsync("a", "b"));
    }
}
