using Portal.Core.Errors;
using Portal.Core.Keys;
using Microsoft.AspNetCore.DataProtection;
using Portal.Security.Authentication;
using Portal.Security.Mfa;
using Portal.Testing;

namespace Portal.UnitTests.Security;

public sealed class AuthServiceTests
{
    private const string Pwd = "Portal!2026";

    private readonly FakeSpExecutor _sp = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly FakePortalConfig _config = new();
    private readonly FakeCurrentUser _user = FakeCurrentUser.Anonymous();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero));

    private readonly DataProtectionMfaSecretProtector _mfa = new(new EphemeralDataProtectionProvider());

    private AuthService Service => new(_sp, _hasher, _config, _user, _time, _mfa, Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthService>.Instance);

    public AuthServiceTests()
    {
        _user.ClientIp = "10.1.2.3";
        // 기본 테스트는 비밀번호만 본다. 2단계 인증은 아래 MFA 테스트에서 켠다.
        _config.Set(ConfigKeys.AuthPartnerMfa, "N");
        _sp.Returns(AuthSp.LoginSuccess, new LoginSession { ExpireDt = new DateTime(2026, 10, 21, 0, 30, 0, DateTimeKind.Utc) })
           .Returns(AuthSp.LoginFail, new LoginFailResult());
    }

    private static AuthUser User(string stat = "OK", string? allowIp = null, string chgReq = "N", DateTime? chgDt = null)
    {
        var hash = FakePasswordHasher.Make(Pwd);
        return new AuthUser
        {
            UserId = "hong",
            CompanyCd = "P0001",
            UserTypeCd = "PARTNER",
            PwdHash = hash.Hash,
            PwdSalt = hash.Salt,
            PwdChgReqYn = chgReq,
            PwdChgDt = chgDt ?? new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            AllowIpList = allowIp,
            AccountStat = stat,
        };
    }

    private void Given(AuthUser? user) => _sp.Returns(AuthSp.GetUser, user);

    private string? LoggedResult() =>
        _sp.Calls.Any(c => c.SpName == AuthSp.LoginFail) ? (string?)_sp.LastCall(AuthSp.LoginFail).Parameters["P_LOGIN_RESULT_CD"] : null;

    [Fact]
    public async Task 성공_새세션토큰_해시만DB로_로그는SP가()
    {
        Given(User());

        var result = await Service.LoginAsync(new LoginRequest(" HONG ", Pwd, "Edge"));

        Assert.True(result.Succeeded);
        Assert.Equal("hong", result.UserId);
        Assert.False(result.PasswordChangeRequired);
        var call = _sp.LastCall(AuthSp.LoginSuccess);
        Assert.Equal(SessionToken.Hash(result.SessionToken!), call.Parameters["P_SESSION_HASH"]);
        Assert.Equal("hong", call.Parameters["P_LOGIN_USER_ID"]);
        Assert.Equal("HONG", _sp.LastCall(AuthSp.GetUser).Parameters["P_LOGIN_USER_ID"]);
        Assert.Null(LoggedResult());
    }

    [Fact]
    public async Task 없는ID_비밀번호오류와같은메시지_비교도한다()
    {
        Given(null);

        var result = await Service.LoginAsync(new LoginRequest("nobody", Pwd, null));

        Assert.False(result.Succeeded);
        Assert.Equal(MessageKeys.LoginFailed, result.MessageKey);
        Assert.Equal(1, _hasher.VerifyCount);
        Assert.Equal(LoginResultCodes.FailPassword, LoggedResult());
        Assert.Equal("nobody", _sp.LastCall(AuthSp.LoginFail).Parameters["P_LOGIN_USER_ID"]);
    }

    [Fact]
    public async Task 비밀번호오류_실패처리()
    {
        Given(User());

        var result = await Service.LoginAsync(new LoginRequest("hong", "wrong", null));

        Assert.Equal(MessageKeys.LoginFailed, result.MessageKey);
        Assert.Equal(LoginResultCodes.FailPassword, LoggedResult());
        Assert.DoesNotContain(_sp.Calls, c => c.SpName == AuthSp.LoginSuccess);
    }

    [Fact]
    public async Task 잠긴계정_비밀번호가틀리면_잠김을알리지않는다()
    {
        Given(User(stat: "LOCKED"));

        var result = await Service.LoginAsync(new LoginRequest("hong", "wrong", null));

        Assert.Equal(MessageKeys.LoginFailed, result.MessageKey);
    }

    [Theory]
    [InlineData("LOCKED", AuthMessageKeys.Locked, LoginResultCodes.FailLocked)]
    [InlineData("EXPIRED", AuthMessageKeys.Expired, LoginResultCodes.FailExpired)]
    public async Task 비밀번호가맞아도_사용불가계정은거부(string stat, string messageKey, string resultCd)
    {
        Given(User(stat: stat));

        var result = await Service.LoginAsync(new LoginRequest("hong", Pwd, null));

        Assert.False(result.Succeeded);
        Assert.Equal(messageKey, result.MessageKey);
        Assert.Equal(resultCd, LoggedResult());
    }

    [Theory]
    [InlineData("10.1.0.0/16", true)]
    [InlineData("192.168.0.0/24", false)]
    [InlineData("잘못된값", false)] // 형식 오류는 막는다
    public async Task 협력사허용IP(string allowIp, bool expected)
    {
        Given(User(allowIp: allowIp));

        var result = await Service.LoginAsync(new LoginRequest("hong", Pwd, null));

        Assert.Equal(expected, result.Succeeded);
        if (!expected)
        {
            Assert.Equal(AuthMessageKeys.IpDenied, result.MessageKey);
            Assert.Equal(LoginResultCodes.FailIp, LoggedResult());
        }
    }

    [Fact]
    public async Task 중복로그인거부정책_SP오류를메시지로()
    {
        Given(User());
        _sp.Throws(AuthSp.LoginSuccess, new BizException(AuthMessageKeys.ConcurrentDenied, AuthSp.ConcurrentDeniedError, new InvalidOperationException()));

        var result = await Service.LoginAsync(new LoginRequest("hong", Pwd, null));

        Assert.Equal(AuthMessageKeys.ConcurrentDenied, result.MessageKey);
        Assert.Equal(LoginResultCodes.FailDuplicate, LoggedResult());
    }

    [Theory]
    [InlineData("", Pwd)]
    [InlineData("hong", "")]
    public async Task 빈입력_DB조회없이실패(string id, string pwd)
    {
        var result = await Service.LoginAsync(new LoginRequest(id, pwd, null));

        Assert.Equal(MessageKeys.LoginFailed, result.MessageKey);
        Assert.Empty(_sp.Calls);
    }

    [Fact]
    public async Task 긴입력_ID는30자_브라우저정보는300자로잘라기록()
    {
        Given(null);

        await Service.LoginAsync(new LoginRequest(new string('a', 50), Pwd, new string('b', 400)));

        var call = _sp.LastCall(AuthSp.LoginFail);
        Assert.Equal(30, ((string)call.Parameters["P_LOGIN_USER_ID"]!).Length);
        Assert.Equal(300, ((string)call.Parameters["P_USER_AGENT"]!).Length);
    }

    [Theory]
    [InlineData("Y", 1, true)]    // 강제 표시
    [InlineData("N", 89, false)]
    [InlineData("N", 91, true)]   // 90일 경과
    public void 비밀번호변경필요(string chgReq, int daysAgo, bool expected) =>
        Assert.Equal(expected, Service.IsPasswordChangeRequired(chgReq, _time.GetUtcNow().UtcDateTime.AddDays(-daysAgo)));

    [Fact]
    public void 비밀번호변경필요_변경이력없음_또는_만료없음설정()
    {
        Assert.True(Service.IsPasswordChangeRequired("N", null));
        _config.Set("PWD.EXPIRE_DAYS", "0");
        Assert.False(Service.IsPasswordChangeRequired("N", new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task 세션확인_정상_종료_모름()
    {
        var (token, hash) = SessionToken.Create();
        _sp.Returns(AuthSp.SessionCheck, p => (string)p["P_SESSION_HASH"]! == hash
            ? new SessionUser { UserId = "hong" }
            : new SessionUser { EndReasonCd = LoginResultCodes.Kicked });

        var ok = await Service.CheckSessionAsync(token);
        var kicked = await Service.CheckSessionAsync(SessionToken.Create().Token);
        var malformed = await Service.CheckSessionAsync("bad");

        Assert.Equal("hong", ok.User!.UserId);
        Assert.Null(kicked.User);
        Assert.Equal(AuthMessageKeys.SessionKicked, kicked.EndMessageKey);
        Assert.Null(malformed.User);
        Assert.Equal(2, _sp.Calls.Count);
    }

    [Fact]
    public async Task 로그아웃_형식이맞는토큰만_해시로()
    {
        var (token, hash) = SessionToken.Create();

        await Service.LogoutAsync("bad", null);
        await Service.LogoutAsync(token, "Edge");

        var call = Assert.Single(_sp.Calls);
        Assert.Equal(hash, call.Parameters["P_SESSION_HASH"]);
    }

    // ---- 2단계 인증 ----

    private static readonly DateTimeOffset Now = new(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);

    private AuthUser MfaUser(byte[]? enrolledSecret = null, long? lastStep = null, string type = "PARTNER", string adminYn = "N")
    {
        var user = User();
        user.UserTypeCd = type;
        user.AdminYn = adminYn;
        user.MfaSecretEnc = enrolledSecret is null ? null : _mfa.Protect(enrolledSecret);
        user.MfaLastStep = lastStep;
        return user;
    }

    [Theory]
    [InlineData("PARTNER", "N", "Y", "N", true)]
    [InlineData("PARTNER", "N", "N", "Y", false)]
    [InlineData("EMP", "Y", "N", "Y", true)]
    [InlineData("EMP", "Y", "N", "N", false)]
    [InlineData("EMP", "N", "Y", "Y", false)]
    public async Task 대상이면_비밀번호가맞아도_세션없이_MFA요구(string type, string adminYn, string partnerMfa, string adminMfa, bool required)
    {
        _config.Set(ConfigKeys.AuthPartnerMfa, partnerMfa).Set(ConfigKeys.AuthAdminMfa, adminMfa);
        Given(MfaUser(type: type, adminYn: adminYn));

        var result = await Service.LoginAsync(new LoginRequest("hong", Pwd, null));

        Assert.Equal(required, result.MfaRequired);
        Assert.Equal(!required, result.Succeeded);
        Assert.Equal(required ? 0 : 1, _sp.Calls.Count(c => c.SpName == AuthSp.LoginSuccess));
        if (required)
        {
            Assert.Null(result.SessionToken);
            Assert.False(result.MfaEnrolled);
            Assert.Equal("hong", result.UserId);
        }
    }

    [Fact]
    public async Task 등록한계정은_MfaEnrolled()
    {
        _config.Set(ConfigKeys.AuthPartnerMfa, "Y");
        Given(MfaUser(Totp.NewSecret()));

        var result = await Service.LoginAsync(new LoginRequest("hong", Pwd, null));

        Assert.True(result.MfaRequired);
        Assert.True(result.MfaEnrolled);
    }

    [Fact]
    public async Task 비밀번호가틀리면_MFA로가지않는다()
    {
        _config.Set(ConfigKeys.AuthPartnerMfa, "Y");
        Given(MfaUser());

        var result = await Service.LoginAsync(new LoginRequest("hong", "wrong", null));

        Assert.False(result.MfaRequired);
        Assert.Equal(MessageKeys.LoginFailed, result.MessageKey);
    }

    [Fact]
    public async Task 처음등록_코드가맞으면_비밀키를암호화저장하고_세션()
    {
        _time.SetUtcNow(Now);
        Given(MfaUser());
        var secret = Totp.NewSecret();

        var result = await Service.VerifyMfaAsync(new MfaVerifyRequest(new MfaTicket("hong", secret), Totp.Code(secret, Totp.StepOf(Now)), "Edge"));

        Assert.True(result.Succeeded);
        var save = _sp.LastCall(AuthSp.MfaSave);
        Assert.Equal(secret, _mfa.Unprotect((byte[])save.Parameters["P_MFA_SECRET_ENC"]!));
        Assert.Equal(Totp.StepOf(Now), save.Parameters["P_MFA_STEP"]);
        var login = _sp.LastCall(AuthSp.LoginSuccess);
        Assert.Equal(AuthMethods.FormMfa, login.Parameters["P_AUTH_METHOD_CD"]);
        Assert.Null(login.Parameters["P_MFA_STEP"]); // 등록 SP가 이미 단계를 적었다
    }

    [Fact]
    public async Task 등록한계정_저장된키로확인하고_단계를세션SP로()
    {
        _time.SetUtcNow(Now);
        var secret = Totp.NewSecret();
        Given(MfaUser(secret, lastStep: Totp.StepOf(Now) - 5));

        var result = await Service.VerifyMfaAsync(new MfaVerifyRequest(new MfaTicket("hong", null), Totp.Code(secret, Totp.StepOf(Now)), null));

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(_sp.Calls, c => c.SpName == AuthSp.MfaSave);
        Assert.Equal(Totp.StepOf(Now), _sp.LastCall(AuthSp.LoginSuccess).Parameters["P_MFA_STEP"]);
    }

    [Fact]
    public async Task 대기표의새키로는_등록한계정을통과할수없다()
    {
        _time.SetUtcNow(Now);
        Given(MfaUser(Totp.NewSecret()));
        var other = Totp.NewSecret();

        var result = await Service.VerifyMfaAsync(new MfaVerifyRequest(new MfaTicket("hong", other), Totp.Code(other, Totp.StepOf(Now)), null));

        Assert.Equal(AuthMessageKeys.MfaInvalid, result.MessageKey);
    }

    [Fact]
    public async Task 이미쓴코드는_재사용거부()
    {
        _time.SetUtcNow(Now);
        var secret = Totp.NewSecret();
        Given(MfaUser(secret, lastStep: Totp.StepOf(Now)));

        var result = await Service.VerifyMfaAsync(new MfaVerifyRequest(new MfaTicket("hong", null), Totp.Code(secret, Totp.StepOf(Now)), null));

        Assert.Equal(AuthMessageKeys.MfaInvalid, result.MessageKey);
        Assert.Equal(LoginResultCodes.FailMfa, LoggedResult());
    }

    [Fact]
    public async Task 틀린코드_실패로세고_잠기면잠김안내()
    {
        _time.SetUtcNow(Now);
        Given(MfaUser(Totp.NewSecret()));
        _sp.Returns(AuthSp.LoginFail, new LoginFailResult { LockYn = "Y" });

        var result = await Service.VerifyMfaAsync(new MfaVerifyRequest(new MfaTicket("hong", null), "000000", null));

        Assert.False(result.Succeeded);
        Assert.Equal(AuthMessageKeys.Locked, result.MessageKey);
        Assert.Equal(LoginResultCodes.FailMfa, LoggedResult());
        Assert.DoesNotContain(_sp.Calls, c => c.SpName == AuthSp.LoginSuccess);
    }

    [Fact]
    public async Task 코드입력사이에_잠긴계정은_거부()
    {
        _time.SetUtcNow(Now);
        var secret = Totp.NewSecret();
        var user = MfaUser(secret);
        user.AccountStat = "LOCKED";
        Given(user);

        var result = await Service.VerifyMfaAsync(new MfaVerifyRequest(new MfaTicket("hong", null), Totp.Code(secret, Totp.StepOf(Now)), null));

        Assert.Equal(AuthMessageKeys.Locked, result.MessageKey);
        Assert.DoesNotContain(_sp.Calls, c => c.SpName == AuthSp.LoginSuccess);
    }

    [Fact]
    public async Task 다른창에서먼저등록했으면_만료로_다시로그인()
    {
        _time.SetUtcNow(Now);
        Given(MfaUser());
        _sp.Throws(AuthSp.MfaSave, new BizException(MessageKeys.Concurrency));
        var secret = Totp.NewSecret();

        var result = await Service.VerifyMfaAsync(new MfaVerifyRequest(new MfaTicket("hong", secret), Totp.Code(secret, Totp.StepOf(Now)), null));

        Assert.Equal(AuthMessageKeys.MfaExpired, result.MessageKey);
        Assert.DoesNotContain(_sp.Calls, c => c.SpName == AuthSp.LoginSuccess);
    }

    [Fact]
    public async Task 같은코드가동시에들어오면_SP가막고_코드오류()
    {
        _time.SetUtcNow(Now);
        var secret = Totp.NewSecret();
        Given(MfaUser(secret));
        _sp.Throws(AuthSp.LoginSuccess, new BizException(AuthMessageKeys.MfaInvalid, AuthSp.MfaReplayError, new InvalidOperationException()));

        var result = await Service.VerifyMfaAsync(new MfaVerifyRequest(new MfaTicket("hong", null), Totp.Code(secret, Totp.StepOf(Now)), null));

        Assert.Equal(AuthMessageKeys.MfaInvalid, result.MessageKey);
        Assert.Equal(LoginResultCodes.FailMfa, LoggedResult());
    }

    [Fact]
    public async Task 저장된비밀키를풀수없으면_만료로_예외없이()
    {
        var user = MfaUser();
        user.MfaSecretEnc = new DataProtectionMfaSecretProtector(new EphemeralDataProtectionProvider()).Protect(Totp.NewSecret());
        Given(user);

        var result = await Service.VerifyMfaAsync(new MfaVerifyRequest(new MfaTicket("hong", null), "123456", null));

        Assert.Equal(AuthMessageKeys.MfaExpired, result.MessageKey);
        Assert.DoesNotContain(_sp.Calls, c => c.SpName == AuthSp.LoginSuccess);
    }
}
