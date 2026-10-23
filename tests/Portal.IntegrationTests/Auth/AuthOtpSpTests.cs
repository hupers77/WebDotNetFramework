using Portal.Core.Keys;
using Portal.IntegrationTests.Infrastructure;
using Portal.Security.Authentication;
using Portal.Security.Mfa;
using Portal.Testing;
using static Portal.IntegrationTests.Auth.AuthTestContext;

namespace Portal.IntegrationTests.Auth;

/// <summary>2단계 인증 이메일 인증번호 (실제 DB): 해시만 저장, 60초 간격·시간당 5통, 5분 유효·5회 입력, 한 번만 사용.</summary>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class AuthOtpSpTests(DbFixture fixture)
{
    private const string Pwd = "Portal!2026";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FakePortalConfig EmailOtpOn() =>
        new FakePortalConfig().Set(ConfigKeys.AuthPartnerMfa, "Y").Set(ConfigKeys.AuthPartnerEmailOtp, "Y");

    private static async Task<TestDb> PartnerAsync(DbFixture fixture, bool withEmail = true)
    {
        var db = await fixture.BeginAsync();
        await db.SeedCompanyAsync("ITO01");
        await SeedLoginUserAsync(db, "it.otp", Pwd, "ITO01", "PARTNER");
        if (withEmail)
        {
            await db.ExecAsync("UPDATE FW.TB_USER SET EMAIL_ENC = 0x0102030405 WHERE USER_ID = 'it.otp'");
        }

        AsAnonymous(db);
        return db;
    }

    private static MfaTicket Ticket => new("it.otp", null);

    [Fact]
    public async Task 보내고_번호가맞으면_세션_번호는해시로만_저장()
    {
        await using var db = await PartnerAsync(fixture);
        var mailer = new FakeMfaCodeMailer();
        var auth = NewAuth(db, EmailOtpOn(), mailer: mailer);

        Assert.True(await auth.CanUseEmailOtpAsync("it.otp", Ct));
        var sent = await auth.SendMfaEmailAsync(Ticket, Ct);
        var code = mailer.LastCode;
        var stored = await db.ScalarAsync<byte[]>("SELECT CODE_HASH FROM FW.TB_AUTH_OTP WHERE USER_ID = 'it.otp'");
        var login = await auth.VerifyMfaAsync(new MfaVerifyRequest(Ticket, code, "Edge", Email: true), Ct);

        Assert.True(sent.Sent);
        Assert.Equal(EmailOtp.Hash("it.otp", code), stored);
        Assert.True(login.Succeeded);
        Assert.Equal("FORM_MFA", await db.ScalarAsync<string>("SELECT AUTH_METHOD_CD FROM LOG.TB_LOGIN_LOG WHERE USER_ID = 'it.otp' AND LOGIN_RESULT_CD = 'SUCCESS'"));
        Assert.Null(await db.ScalarAsync<byte[]?>("SELECT MFA_SECRET_ENC FROM FW.TB_USER WHERE USER_ID = 'it.otp'"));
    }

    [Fact]
    public async Task 한번쓴번호는_다시쓸수없다()
    {
        await using var db = await PartnerAsync(fixture);
        var mailer = new FakeMfaCodeMailer();
        var auth = NewAuth(db, EmailOtpOn(), mailer: mailer);
        await auth.SendMfaEmailAsync(Ticket, Ct);
        var code = mailer.LastCode;

        var first = await auth.VerifyMfaAsync(new MfaVerifyRequest(Ticket, code, null, Email: true), Ct);
        var second = await auth.VerifyMfaAsync(new MfaVerifyRequest(Ticket, code, null, Email: true), Ct);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(AuthMessageKeys.OtpInvalid, second.MessageKey);
    }

    [Fact]
    public async Task 직전발송_1분안에는_다시보낼수없다()
    {
        await using var db = await PartnerAsync(fixture);
        var mailer = new FakeMfaCodeMailer();
        var auth = NewAuth(db, EmailOtpOn(), mailer: mailer);

        await auth.SendMfaEmailAsync(Ticket, Ct);
        var again = await auth.SendMfaEmailAsync(Ticket, Ct);

        Assert.False(again.Sent);
        Assert.Equal(AuthMessageKeys.OtpTooSoon, again.MessageKey);
        Assert.Single(mailer.Sent);

        await db.ExecAsync("UPDATE FW.TB_AUTH_OTP SET SEND_DT = DATEADD(SECOND, -61, SEND_DT) WHERE USER_ID = 'it.otp'");
        Assert.True((await auth.SendMfaEmailAsync(Ticket, Ct)).Sent);
    }

    [Fact]
    public async Task 한시간에_5통까지_6번째는_한도()
    {
        await using var db = await PartnerAsync(fixture);
        var mailer = new FakeMfaCodeMailer();
        var auth = NewAuth(db, EmailOtpOn(), mailer: mailer);

        for (var i = 0; i < 5; i++)
        {
            Assert.True((await auth.SendMfaEmailAsync(Ticket, Ct)).Sent);
            await db.ExecAsync("UPDATE FW.TB_AUTH_OTP SET SEND_DT = DATEADD(SECOND, -61, SEND_DT) WHERE USER_ID = 'it.otp'");
        }

        var sixth = await auth.SendMfaEmailAsync(Ticket, Ct);
        Assert.Equal(AuthMessageKeys.OtpLimit, sixth.MessageKey);

        // 구간(1시간)이 지나면 다시 센다
        await db.ExecAsync("UPDATE FW.TB_AUTH_OTP SET WINDOW_DT = DATEADD(MINUTE, -61, WINDOW_DT) WHERE USER_ID = 'it.otp'");
        Assert.True((await auth.SendMfaEmailAsync(Ticket, Ct)).Sent);
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT SEND_CNT FROM FW.TB_AUTH_OTP WHERE USER_ID = 'it.otp'"));
    }

    [Fact]
    public async Task 유효시간이지나면_맞는번호도_받지않는다()
    {
        await using var db = await PartnerAsync(fixture);
        var mailer = new FakeMfaCodeMailer();
        var auth = NewAuth(db, EmailOtpOn(), mailer: mailer);
        await auth.SendMfaEmailAsync(Ticket, Ct);
        await db.ExecAsync("UPDATE FW.TB_AUTH_OTP SET EXPIRE_DT = DATEADD(SECOND, -1, SYSUTCDATETIME()) WHERE USER_ID = 'it.otp'");

        var login = await auth.VerifyMfaAsync(new MfaVerifyRequest(Ticket, mailer.LastCode, null, Email: true), Ct);

        Assert.False(login.Succeeded);
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM LOG.TB_LOGIN_LOG WHERE USER_ID = 'it.otp' AND LOGIN_RESULT_CD = 'FAIL_MFA'"));
    }

    [Fact]
    public async Task 틀린번호는_번호당5회까지_그뒤에는_맞는번호도_받지않는다()
    {
        await using var db = await PartnerAsync(fixture);
        var mailer = new FakeMfaCodeMailer();
        var auth = NewAuth(db, EmailOtpOn(), mailer: mailer);
        await auth.SendMfaEmailAsync(Ticket, Ct);
        var right = mailer.LastCode;
        var wrong = right == "000000" ? "111111" : "000000";

        // 잠금(PWD.LOCK_COUNT 5회)에 닿지 않게 로그인 실패 횟수만 되돌리며 5회를 채운다
        for (var i = 0; i < 5; i++)
        {
            Assert.False((await auth.VerifyMfaAsync(new MfaVerifyRequest(Ticket, wrong, null, Email: true), Ct)).Succeeded);
            await db.ExecAsync("UPDATE FW.TB_USER SET LOGIN_FAIL_CNT = 0, LOCK_YN = 'N', LOCK_DT = NULL WHERE USER_ID = 'it.otp'");
        }

        var late = await auth.VerifyMfaAsync(new MfaVerifyRequest(Ticket, right, null, Email: true), Ct);

        Assert.False(late.Succeeded);
        Assert.Equal(5, await db.ScalarAsync<int>("SELECT TRY_CNT FROM FW.TB_AUTH_OTP WHERE USER_ID = 'it.otp'"));
    }

    [Fact]
    public async Task 이메일없는계정은_허용되지않고_발송도_안된다()
    {
        await using var db = await PartnerAsync(fixture, withEmail: false);
        var mailer = new FakeMfaCodeMailer();
        var auth = NewAuth(db, EmailOtpOn(), mailer: mailer);

        Assert.False(await auth.CanUseEmailOtpAsync("it.otp", Ct));
        Assert.False((await auth.SendMfaEmailAsync(Ticket, Ct)).Sent);
        Assert.Empty(mailer.Sent);
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_AUTH_OTP WHERE USER_ID = 'it.otp'"));
    }

    [Fact]
    public async Task 허용설정을끄면_발송과확인모두거절()
    {
        await using var db = await PartnerAsync(fixture);
        var mailer = new FakeMfaCodeMailer();
        var on = NewAuth(db, EmailOtpOn(), mailer: mailer);
        await on.SendMfaEmailAsync(Ticket, Ct);
        var off = NewAuth(db, new FakePortalConfig().Set(ConfigKeys.AuthPartnerMfa, "Y").Set(ConfigKeys.AuthPartnerEmailOtp, "N"), mailer: mailer);

        Assert.False((await off.SendMfaEmailAsync(Ticket, Ct)).Sent);
        Assert.False((await off.VerifyMfaAsync(new MfaVerifyRequest(Ticket, mailer.LastCode, null, Email: true), Ct)).Succeeded);
    }
}
