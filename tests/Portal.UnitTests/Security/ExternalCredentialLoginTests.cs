using Microsoft.AspNetCore.DataProtection;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Security.Authentication;
using Portal.Security.Mfa;
using Portal.Testing;

namespace Portal.UnitTests.Security;

/// <summary>외부 검증기(AD) 로그인 확장 지점: 검증기 분기, 비밀번호 변경 강제 제외, AD_MFA.</summary>
public sealed class ExternalCredentialLoginTests
{
    private readonly FakeSpExecutor _sp = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly FakePortalConfig _config = new();
    private readonly FakeCurrentUser _user = FakeCurrentUser.Anonymous();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero));
    private readonly FakeMfaCodeMailer _mailer = new();
    private readonly DataProtectionMfaSecretProtector _mfa = new(new EphemeralDataProtectionProvider());
    private readonly StubValidator _validator = new();

    public ExternalCredentialLoginTests()
    {
        _user.ClientIp = "10.1.2.3";
        _config.Set(ConfigKeys.AuthPartnerMfa, "Y").Set(ConfigKeys.AuthAdMfa, "N");
        _sp.Returns(AuthSp.LoginSuccess, new LoginSession { ExpireDt = new DateTime(2026, 11, 5, 0, 30, 0, DateTimeKind.Utc) })
           .Returns(AuthSp.LoginFail, new LoginFailResult());
    }

    private AuthService Service(bool withValidator = true) =>
        new(_sp, _hasher, _config, _user, _time, _mfa, _mailer,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthService>.Instance,
            withValidator ? new[] { _validator } : null);

    // AD 전용 계정: 포털 비밀번호 해시와 변경 이력이 없다
    private void GivenAdUser(string type = "PARTNER", string adminYn = "N") =>
        _sp.Returns(AuthSp.GetUser, new AuthUser
        {
            UserId = "iv0106533",
            CompanyCd = "HQ",
            UserTypeCd = type,
            AdminYn = adminYn,
            AccountStat = "OK",
        });

    private string? LoggedResult() =>
        _sp.Calls.Any(c => c.SpName == AuthSp.LoginFail) ? (string?)_sp.LastCall(AuthSp.LoginFail).Parameters["P_LOGIN_RESULT_CD"] : null;

    [Fact]
    public async Task 검증기_성공_PWD_HASH가_없어도_AD방식_세션_비밀번호변경_강제없음()
    {
        GivenAdUser();
        _validator.Result = new ExternalCredentialResult(true);

        var result = await Service().LoginAsync(new LoginRequest("iv0106533", "ad-secret", null));

        Assert.True(result.Succeeded);
        Assert.False(result.PasswordChangeRequired);
        Assert.Equal(AuthMethods.Ad, _sp.LastCall(AuthSp.LoginSuccess).Parameters["P_AUTH_METHOD_CD"]);
        Assert.Equal("ad-secret", _validator.LastPassword);
        Assert.Equal(0, _hasher.VerifyCount);
    }

    [Fact]
    public async Task 검증기_실패_실패횟수에_넣는다()
    {
        GivenAdUser();
        _validator.Result = new ExternalCredentialResult(false, "BAD_PASSWORD");

        var result = await Service().LoginAsync(new LoginRequest("iv0106533", "wrong", null));

        Assert.False(result.Succeeded);
        Assert.Equal(MessageKeys.LoginFailed, result.MessageKey);
        Assert.Equal(LoginResultCodes.FailPassword, LoggedResult());
        Assert.DoesNotContain(_sp.Calls, c => c.SpName == AuthSp.LoginSuccess);
    }

    [Fact]
    public async Task 검증기_서버장애_비밀번호오류로_세지_않는다()
    {
        GivenAdUser();
        _validator.Result = new ExternalCredentialResult(false, ExternalCredentialResult.Unavailable);

        var result = await Service().LoginAsync(new LoginRequest("iv0106533", "pw", null));

        Assert.Equal(AuthMessageKeys.ExternalUnavailable, result.MessageKey);
        Assert.Null(LoggedResult());
    }

    [Fact]
    public async Task 검증기_예외_장애로_처리하고_비밀번호를_남기지_않는다()
    {
        GivenAdUser();
        _validator.Throw = new InvalidOperationException("bind failed for secret-pw");

        var result = await Service().LoginAsync(new LoginRequest("iv0106533", "secret-pw", null));

        Assert.Equal(AuthMessageKeys.ExternalUnavailable, result.MessageKey);
        Assert.Null(LoggedResult());
    }

    [Fact]
    public async Task 검증기_대상이_아니면_포털_비밀번호로_확인()
    {
        GivenAdUser();
        _validator.Handles = false;

        var result = await Service().LoginAsync(new LoginRequest("iv0106533", "pw", null));

        Assert.False(result.Succeeded); // 해시가 없는 계정은 포털 비밀번호로 로그인할 수 없다
        Assert.Equal(0, _validator.ValidateCount);
    }

    [Fact]
    public async Task 검증기_미등록_기존_동작()
    {
        GivenAdUser();

        var result = await Service(withValidator: false).LoginAsync(new LoginRequest("iv0106533", "pw", null));

        Assert.False(result.Succeeded);
        Assert.Equal(LoginResultCodes.FailPassword, LoggedResult());
    }

    [Fact]
    public async Task AUTH_MODE가_FORM이면_검증기를_쓰지_않는다()
    {
        GivenAdUser();
        _config.Set(ConfigKeys.AuthMode, "FORM");
        _validator.Result = new ExternalCredentialResult(true);

        var result = await Service().LoginAsync(new LoginRequest("iv0106533", "pw", null));

        Assert.False(result.Succeeded);
        Assert.Equal(0, _validator.ValidateCount);
    }

    [Fact]
    public async Task AD_MFA가_Y이면_2단계인증을_거친다()
    {
        GivenAdUser(type: "EMP");
        _config.Set(ConfigKeys.AuthAdMfa, "Y");
        _validator.Result = new ExternalCredentialResult(true);

        var result = await Service().LoginAsync(new LoginRequest("iv0106533", "pw", null));

        Assert.True(result.MfaRequired);
        Assert.DoesNotContain(_sp.Calls, c => c.SpName == AuthSp.LoginSuccess);
    }

    [Fact]
    public async Task AD_MFA가_N이면_협력사_유형이어도_2단계인증_없음()
    {
        GivenAdUser(type: "PARTNER"); // 협력사 설정(AUTH.PARTNER_MFA = Y)은 AD 로그인에 적용하지 않는다
        _validator.Result = new ExternalCredentialResult(true);

        var result = await Service().LoginAsync(new LoginRequest("iv0106533", "pw", null));

        Assert.True(result.Succeeded);
        Assert.False(result.MfaRequired);
    }

    [Fact]
    public async Task AD_MFA가_N이어도_관리자_역할은_2단계인증_유지()
    {
        GivenAdUser(type: "EMP", adminYn: "Y");
        _validator.Result = new ExternalCredentialResult(true);

        var result = await Service().LoginAsync(new LoginRequest("iv0106533", "pw", null));

        Assert.True(result.MfaRequired);
    }

    [Theory]
    [InlineData(AuthMethods.Ad, false)]
    [InlineData(AuthMethods.AdMfa, false)]
    [InlineData(AuthMethods.Form, true)]
    [InlineData(null, true)]
    public void 비밀번호_변경_강제는_AD_세션에_적용하지_않는다(string? authMethod, bool expected) =>
        Assert.Equal(expected, Service().IsPasswordChangeRequired("N", null, authMethod));

    private sealed class StubValidator : IExternalCredentialValidator
    {
        public bool Handles { get; set; } = true;

        public ExternalCredentialResult Result { get; set; } = new(false);

        public Exception? Throw { get; set; }

        public string? LastPassword { get; private set; }

        public int ValidateCount { get; private set; }

        public Task<bool> CanHandleAsync(string userId, CancellationToken cancellationToken) => Task.FromResult(Handles);

        public Task<ExternalCredentialResult> ValidateAsync(string userId, string password, CancellationToken cancellationToken)
        {
            ValidateCount++;
            LastPassword = password;
            return Throw is null ? Task.FromResult(Result) : throw Throw;
        }
    }
}
