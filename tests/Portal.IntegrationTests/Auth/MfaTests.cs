using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.IntegrationTests.Infrastructure;
using Portal.Security.Authentication;
using Portal.Security.Mfa;
using Portal.Testing;
using static Portal.IntegrationTests.Auth.AuthTestContext;

namespace Portal.IntegrationTests.Auth;

/// <summary>2단계 인증(TOTP): 대상 판단, 첫 등록, 코드 재사용 방지, 실패 잠금 (설계서 5장).</summary>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class MfaTests(DbFixture fixture)
{
    private const string Pwd = "Portal!2026";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // 시간 단계 경계에서 흔들리지 않게 단계 가운데 시각으로 고정한다
    private static readonly FakeTimeProvider Time = new(DateTimeOffset.FromUnixTimeSeconds((Totp.StepOf(DateTimeOffset.UtcNow) * Totp.StepSeconds) + 15));

    private static long Step => Totp.StepOf(Time.GetUtcNow());

    private static async Task<TestDb> PartnerAsync(DbFixture fixture)
    {
        var db = await fixture.BeginAsync();
        await db.SeedCompanyAsync("ITM01");
        await SeedLoginUserAsync(db, "it.mfa", Pwd, "ITM01", "PARTNER");
        AsAnonymous(db);
        return db;
    }

    private static Task<long?> LastStepAsync(TestDb db) =>
        db.ScalarAsync<long?>("SELECT MFA_LAST_STEP FROM FW.TB_USER WHERE USER_ID = 'it.mfa'");

    /// <summary>등록을 마친 상태로 만든다.</summary>
    private static async Task<byte[]> EnrollAsync(TestDb db, long step)
    {
        var secret = Totp.NewSecret();
        await db.ExecAsync(
            "UPDATE FW.TB_USER SET MFA_SECRET_ENC = @E, MFA_LAST_STEP = @S WHERE USER_ID = 'it.mfa'",
            ("@E", MfaSecrets.Protect(secret)), ("@S", step));
        return secret;
    }

    [Fact]
    public async Task 대상판단_협력사와관리자역할만_설정으로끈다()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedCompanyAsync("ITM01");
        await SeedLoginUserAsync(db, "it.ptn", Pwd, "ITM01", "PARTNER");
        await SeedLoginUserAsync(db, "it.emp", Pwd);
        await SeedLoginUserAsync(db, "it.adm", Pwd, roles: "PORTAL_ADMIN");
        AsAnonymous(db);
        var auth = NewAuth(db);

        Assert.True((await auth.LoginAsync(new LoginRequest("it.ptn", Pwd, null), Ct)).MfaRequired);
        Assert.True((await auth.LoginAsync(new LoginRequest("it.adm", Pwd, null), Ct)).MfaRequired);
        Assert.True((await auth.LoginAsync(new LoginRequest("it.emp", Pwd, null), Ct)).Succeeded);
        Assert.True((await NewAuth(db, PasswordOnly()).LoginAsync(new LoginRequest("it.adm", Pwd, null), Ct)).Succeeded);
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_USER_SESSION WHERE USER_ID = 'it.ptn'"));
    }

    [Fact]
    public async Task 첫등록_코드가맞으면_암호화저장_단계기록_감사로그_FORM_MFA세션()
    {
        await using var db = await PartnerAsync(fixture);
        var auth = NewAuth(db, time: Time);
        var login = await auth.LoginAsync(new LoginRequest("it.mfa", Pwd, null), Ct);
        Assert.True(login is { MfaRequired: true, MfaEnrolled: false });
        var secret = Totp.NewSecret();

        var result = await auth.VerifyMfaAsync(new MfaVerifyRequest(new MfaTicket("it.mfa", secret), Totp.Code(secret, Step), "IT-Agent"), Ct);

        Assert.True(result.Succeeded, result.MessageKey);
        var stored = await db.ScalarAsync<byte[]>("SELECT MFA_SECRET_ENC FROM FW.TB_USER WHERE USER_ID = 'it.mfa'");
        Assert.Equal(secret, MfaSecrets.Unprotect(stored));
        Assert.Equal(Step, await LastStepAsync(db));
        Assert.Equal(AuthMethods.FormMfa, await db.ScalarAsync<string>(
            "SELECT AUTH_METHOD_CD FROM FW.TB_USER_SESSION WHERE SESSION_ID = @H", ("@H", SessionToken.Hash(result.SessionToken!))));
        Assert.Equal(AuthMethods.FormMfa, await db.ScalarAsync<string>(
            "SELECT TOP 1 AUTH_METHOD_CD FROM LOG.TB_LOGIN_LOG WHERE USER_ID = 'it.mfa' ORDER BY LOG_ID DESC"));
        Assert.Equal("{\"MFA_ENROLLED\":true}", await db.ScalarAsync<string>(
            "SELECT AFTER_JSON FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_USER' AND ROW_KEY = 'it.mfa'"));
        Assert.True((await auth.LoginAsync(new LoginRequest("it.mfa", Pwd, null), Ct)).MfaEnrolled);
    }

    [Fact]
    public async Task 같은코드로_두번로그인할수없다_다음단계코드는된다()
    {
        await using var db = await PartnerAsync(fixture);
        var secret = await EnrollAsync(db, Step - 10);
        var auth = NewAuth(db, time: Time);
        var ticket = new MfaTicket("it.mfa", null);

        var first = await auth.VerifyMfaAsync(new MfaVerifyRequest(ticket, Totp.Code(secret, Step), null), Ct);
        var again = await auth.VerifyMfaAsync(new MfaVerifyRequest(ticket, Totp.Code(secret, Step), null), Ct);
        var older = await auth.VerifyMfaAsync(new MfaVerifyRequest(ticket, Totp.Code(secret, Step - 1), null), Ct);
        var next = await auth.VerifyMfaAsync(new MfaVerifyRequest(ticket, Totp.Code(secret, Step + 1), null), Ct);

        Assert.True(first.Succeeded, first.MessageKey);
        Assert.Equal(AuthMessageKeys.MfaInvalid, again.MessageKey);
        Assert.Equal(AuthMessageKeys.MfaInvalid, older.MessageKey);
        Assert.True(next.Succeeded, next.MessageKey);
        Assert.Equal(Step + 1, await LastStepAsync(db));
        Assert.Equal(LoginResultCodes.FailMfa, await db.ScalarAsync<string>(
            "SELECT TOP 1 LOGIN_RESULT_CD FROM LOG.TB_LOGIN_LOG WHERE USER_ID = 'it.mfa' AND LOGIN_RESULT_CD <> 'SUCCESS' ORDER BY LOG_ID DESC"));
    }

    [Fact]
    public async Task 틀린코드도_실패횟수로세어_잠근다()
    {
        await using var db = await PartnerAsync(fixture);
        var secret = await EnrollAsync(db, Step - 10);
        var auth = NewAuth(db, time: Time);
        var lockCount = int.Parse(await db.ScalarAsync<string>("SELECT CONFIG_VAL FROM FW.TB_CONFIG WHERE CONFIG_KEY = 'PWD.LOCK_COUNT'"), System.Globalization.CultureInfo.InvariantCulture);
        var wrong = Totp.Code(secret, Step) == "000000" ? "111111" : "000000";

        LoginResult last = null!;
        for (var i = 0; i < lockCount; i++)
        {
            last = await auth.VerifyMfaAsync(new MfaVerifyRequest(new MfaTicket("it.mfa", null), wrong, null), Ct);
        }

        Assert.Equal(AuthMessageKeys.Locked, last.MessageKey);
        Assert.Equal(AuthMessageKeys.Locked, (await auth.VerifyMfaAsync(new MfaVerifyRequest(new MfaTicket("it.mfa", null), Totp.Code(secret, Step), null), Ct)).MessageKey);
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_USER_SESSION WHERE USER_ID = 'it.mfa'"));
    }

    [Fact]
    public async Task 세션SP가_이전단계를다시받으면_오류()
    {
        await using var db = await PartnerAsync(fixture);
        await EnrollAsync(db, 100);

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.QuerySingleAsync<LoginSession>(
            AuthSp.LoginSuccess,
            new { P_LOGIN_USER_ID = "it.mfa", P_SESSION_HASH = new string('a', 64), P_AUTH_METHOD_CD = AuthMethods.FormMfa, P_USER_AGENT = (string?)null, P_MFA_STEP = 100L },
            Ct));

        Assert.Equal(AuthSp.MfaReplayError, ex.SqlErrorNumber);
    }

    [Fact]
    public async Task 등록SP는_이미등록한계정을_덮어쓰지않는다()
    {
        await using var db = await PartnerAsync(fixture);
        await EnrollAsync(db, 100);

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync(
            AuthSp.MfaSave,
            new { P_LOGIN_USER_ID = "it.mfa", P_MFA_SECRET_ENC = new byte[] { 1, 2, 3 }, P_MFA_STEP = 200L },
            Ct));

        Assert.Equal(MessageKeys.Concurrency, ex.MessageKey);
    }
}
