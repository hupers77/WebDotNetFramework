using Portal.Security.Authentication;
using Portal.Security.Passwords;
using Portal.Testing;

namespace Portal.UnitTests.Security;

public sealed class AdminBootstrapServiceTests
{
    [Fact]
    public void 임시비밀번호_4종류16자_기본정책통과()
    {
        var policy = new PasswordPolicy(new FakePortalConfig());
        for (var i = 0; i < 200; i++)
        {
            var pwd = AdminBootstrapService.GenerateTempPassword();

            Assert.Equal(16, pwd.Length);
            Assert.Equal(4, PasswordPolicy.CountTypes(pwd));
            Assert.Empty(policy.Validate(pwd, "sysadmin"));
        }
    }

    [Fact]
    public async Task InitAsync_해시만SP로_원문은호출자에게만()
    {
        var sp = new FakeSpExecutor().Returns(AuthSp.AdminInit, new AdminInitResult { CreatedYn = "Y" });
        var hasher = new FakePasswordHasher();

        var (created, temp) = await new AdminBootstrapService(sp, hasher).InitAsync();

        Assert.True(created);
        var call = sp.LastCall(AuthSp.AdminInit);
        Assert.Equal(FakePasswordHasher.Make(temp).Hash, call.Parameters["P_PWD_HASH"]);
        Assert.DoesNotContain(call.Parameters.Values, v => v is string s && s == temp);
    }
}
