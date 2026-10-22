using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.IntegrationTests.Infrastructure;
using Portal.Security.Authentication;
using static Portal.IntegrationTests.Auth.AuthTestContext;

namespace Portal.IntegrationTests.Auth;

/// <summary>ID/PW 로그인, 실패 잠금, 중복 로그인, 세션 확인·만료, 로그아웃 (설계서 5장).</summary>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class LoginTests(DbFixture fixture)
{
    private const string Pwd = "Portal!2026";

    private static LoginRequest Req(string userId, string password = Pwd) => new(userId, password, "IT-Agent");

    private static Task<string> LastLoginResultAsync(TestDb db, string userId) =>
        db.ScalarAsync<string>("SELECT TOP 1 LOGIN_RESULT_CD FROM LOG.TB_LOGIN_LOG WHERE USER_ID = @U ORDER BY LOG_ID DESC", ("@U", userId));

    [Fact]
    public async Task 성공_세션은해시로저장_로그인로그와마지막로그인시각()
    {
        await using var db = await fixture.BeginAsync();
        await SeedLoginUserAsync(db, "it.hong", Pwd);
        AsAnonymous(db);

        var result = await NewAuth(db).LoginAsync(Req("IT.HONG"));

        Assert.True(result.Succeeded);
        Assert.Equal("it.hong", result.UserId);
        Assert.Equal(1, await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM FW.TB_USER_SESSION WHERE SESSION_ID = @H AND USER_ID = 'it.hong' AND END_DT IS NULL AND CLIENT_IP = '10.1.2.3'",
            ("@H", SessionToken.Hash(result.SessionToken!))));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_USER_SESSION WHERE SESSION_ID = @T", ("@T", result.SessionToken!)));
        Assert.Equal(LoginResultCodes.Success, await LastLoginResultAsync(db, "it.hong"));
        Assert.NotNull(await db.ScalarAsync<DateTime?>("SELECT LAST_LOGIN_DT FROM FW.TB_USER WHERE USER_ID = 'it.hong'"));
    }

    [Fact]
    public async Task 없는ID_입력값으로실패로그()
    {
        await using var db = await fixture.BeginAsync();
        AsAnonymous(db);

        var result = await NewAuth(db).LoginAsync(Req("it.nobody"));

        Assert.Equal(MessageKeys.LoginFailed, result.MessageKey);
        Assert.Equal(LoginResultCodes.FailPassword, await LastLoginResultAsync(db, "it.nobody"));
    }

    [Fact]
    public async Task 실패5회_잠금_맞는비밀번호도거부_자동해제시간후성공()
    {
        await using var db = await fixture.BeginAsync();
        await SeedLoginUserAsync(db, "it.hong", Pwd);
        AsAnonymous(db);
        var auth = NewAuth(db);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(MessageKeys.LoginFailed, (await auth.LoginAsync(Req("it.hong", "wrong"))).MessageKey);
        }

        Assert.Equal("Y", await db.ScalarAsync<string>("SELECT LOCK_YN FROM FW.TB_USER WHERE USER_ID = 'it.hong'"));
        Assert.Equal(AuthMessageKeys.Locked, (await auth.LoginAsync(Req("it.hong"))).MessageKey);
        Assert.Equal(LoginResultCodes.FailLocked, await LastLoginResultAsync(db, "it.hong"));

        // PWD.LOCK_MINUTES(30분)이 지나면 자동 해제
        await db.ExecAsync("UPDATE FW.TB_USER SET LOCK_DT = DATEADD(MINUTE, -31, SYSUTCDATETIME()) WHERE USER_ID = 'it.hong'");
        Assert.True((await auth.LoginAsync(Req("it.hong"))).Succeeded);
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT LOGIN_FAIL_CNT FROM FW.TB_USER WHERE USER_ID = 'it.hong' AND LOCK_YN = 'N'"));
    }

    [Fact]
    public async Task 자동해제시간이지난잠금_실패횟수를새로센다()
    {
        await using var db = await fixture.BeginAsync();
        await SeedLoginUserAsync(db, "it.hong", Pwd);
        await db.ExecAsync("UPDATE FW.TB_USER SET LOCK_YN = 'Y', LOGIN_FAIL_CNT = 5, LOCK_DT = DATEADD(MINUTE, -31, SYSUTCDATETIME()) WHERE USER_ID = 'it.hong'");
        AsAnonymous(db);

        await NewAuth(db).LoginAsync(Req("it.hong", "wrong"));

        Assert.Equal(1, await db.ScalarAsync<int>("SELECT LOGIN_FAIL_CNT FROM FW.TB_USER WHERE USER_ID = 'it.hong' AND LOCK_YN = 'N'"));
    }

    [Theory]
    [InlineData("UPDATE FW.TB_USER SET USER_STAT_CD = 'RETIRED' WHERE USER_ID = 'it.part'")]
    [InlineData("UPDATE FW.TB_USER SET ACCOUNT_END_DT = DATEADD(DAY, -1, CAST(GETDATE() AS DATE)) WHERE USER_ID = 'it.part'")]
    [InlineData("UPDATE FW.TB_COMPANY SET CONTRACT_END_DT = DATEADD(DAY, -1, CAST(GETDATE() AS DATE)) WHERE COMPANY_CD = 'ITP01'")]
    [InlineData("UPDATE FW.TB_COMPANY SET USE_YN = 'N' WHERE COMPANY_CD = 'ITP01'")]
    public async Task 사용할수없는계정_만료로거부(string setup)
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedCompanyAsync("ITP01");
        await SeedLoginUserAsync(db, "it.part", Pwd, "ITP01", "PARTNER");
        await db.ExecAsync(setup);
        AsAnonymous(db);

        var result = await NewAuth(db).LoginAsync(Req("it.part"));

        Assert.Equal(AuthMessageKeys.Expired, result.MessageKey);
        Assert.Equal(LoginResultCodes.FailExpired, await LastLoginResultAsync(db, "it.part"));
    }

    [Theory]
    [InlineData("10.1.0.0/16", true)]
    [InlineData("192.168.10.0/24", false)]
    public async Task 협력사허용IP(string allowIp, bool expected)
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedCompanyAsync("ITP01");
        await db.ExecAsync("UPDATE FW.TB_COMPANY SET ALLOW_IP_LIST = @L WHERE COMPANY_CD = 'ITP01'", ("@L", allowIp));
        await SeedLoginUserAsync(db, "it.part", Pwd, "ITP01", "PARTNER");
        AsAnonymous(db, "10.1.2.3");

        var result = await NewAuth(db, PasswordOnly()).LoginAsync(Req("it.part"));

        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public async Task 최초로그인_비밀번호변경필요()
    {
        await using var db = await fixture.BeginAsync();
        await SeedLoginUserAsync(db, "it.hong", Pwd);
        await db.ExecAsync("UPDATE FW.TB_USER SET PWD_CHG_REQ_YN = 'Y' WHERE USER_ID = 'it.hong'");
        AsAnonymous(db);

        Assert.True((await NewAuth(db).LoginAsync(Req("it.hong"))).PasswordChangeRequired);
    }

    [Fact]
    public async Task 중복로그인_기본정책은기존세션종료_기존세션확인시KICKED()
    {
        await using var db = await fixture.BeginAsync();
        await SeedLoginUserAsync(db, "it.hong", Pwd);
        AsAnonymous(db);
        var auth = NewAuth(db);

        var first = await auth.LoginAsync(Req("it.hong"));
        var second = await auth.LoginAsync(Req("it.hong"));

        Assert.True(second.Succeeded);
        var check = await auth.CheckSessionAsync(first.SessionToken);
        Assert.Null(check.User);
        Assert.Equal(LoginResultCodes.Kicked, check.EndReasonCd);
        Assert.Equal("it.hong", (await auth.CheckSessionAsync(second.SessionToken)).User!.UserId);
    }

    [Fact]
    public async Task 중복로그인_DENY_NEW정책_SP가거부()
    {
        await using var db = await fixture.BeginAsync();
        await SeedLoginUserAsync(db, "it.hong", Pwd);
        await db.ExecAsync("UPDATE FW.TB_CONFIG SET CONFIG_VAL = 'DENY_NEW' WHERE CONFIG_KEY = 'SESSION.CONCURRENT_POLICY'");
        AsAnonymous(db);
        Assert.True((await NewAuth(db).LoginAsync(Req("it.hong"))).Succeeded);

        // SP 오류는 테스트 트랜잭션까지 롤백하므로 서비스 대신 SP를 직접 호출해 확인하고 끝낸다
        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync(
            "FW.USP_AUTH_LOGIN_SUCCESS_EXEC",
            new { P_LOGIN_USER_ID = "it.hong", P_SESSION_HASH = SessionToken.Create().Hash, P_AUTH_METHOD_CD = "FORM", P_USER_AGENT = (string?)null },
            TestContext.Current.CancellationToken));
        Assert.Equal(AuthMessageKeys.ConcurrentDenied, ex.MessageKey);
        Assert.Equal(50010, ex.SqlErrorNumber);
    }

    [Fact]
    public async Task 세션확인_비활동만료_TIMEOUT()
    {
        await using var db = await fixture.BeginAsync();
        await SeedLoginUserAsync(db, "it.hong", Pwd);
        AsAnonymous(db);
        var auth = NewAuth(db);
        var login = await auth.LoginAsync(Req("it.hong"));

        await db.ExecAsync("UPDATE FW.TB_USER_SESSION SET EXPIRE_DT = DATEADD(SECOND, -1, SYSUTCDATETIME()) WHERE USER_ID = 'it.hong'");
        var check = await auth.CheckSessionAsync(login.SessionToken);

        Assert.Equal(LoginResultCodes.Timeout, check.EndReasonCd);
        Assert.Equal(AuthMessageKeys.SessionExpired, check.EndMessageKey);
        Assert.Equal("TIMEOUT", await db.ScalarAsync<string>("SELECT END_REASON_CD FROM FW.TB_USER_SESSION WHERE USER_ID = 'it.hong' AND END_DT IS NOT NULL"));
    }

    [Fact]
    public async Task 세션확인_로그인중잠긴계정_DISABLED()
    {
        await using var db = await fixture.BeginAsync();
        await SeedLoginUserAsync(db, "it.hong", Pwd);
        AsAnonymous(db);
        var auth = NewAuth(db);
        var login = await auth.LoginAsync(Req("it.hong"));

        await db.ExecAsync("UPDATE FW.TB_USER SET USER_STAT_CD = 'LOCKED' WHERE USER_ID = 'it.hong'");

        Assert.Equal(LoginResultCodes.Disabled, (await auth.CheckSessionAsync(login.SessionToken)).EndReasonCd);
    }

    [Fact]
    public async Task 세션확인_1분지나면_만료시각연장()
    {
        await using var db = await fixture.BeginAsync();
        await SeedLoginUserAsync(db, "it.hong", Pwd);
        AsAnonymous(db);
        var auth = NewAuth(db);
        var login = await auth.LoginAsync(Req("it.hong"));
        await db.ExecAsync("UPDATE FW.TB_USER_SESSION SET LAST_ACCESS_DT = DATEADD(MINUTE, -10, LAST_ACCESS_DT), EXPIRE_DT = DATEADD(MINUTE, -10, EXPIRE_DT) WHERE USER_ID = 'it.hong'");
        var before = await db.ScalarAsync<DateTime>("SELECT EXPIRE_DT FROM FW.TB_USER_SESSION WHERE USER_ID = 'it.hong'");

        var check = await auth.CheckSessionAsync(login.SessionToken);

        Assert.True(check.User!.ExpireDt > before.AddMinutes(9));
        Assert.Equal(check.User.ExpireDt, await db.ScalarAsync<DateTime>("SELECT EXPIRE_DT FROM FW.TB_USER_SESSION WHERE USER_ID = 'it.hong'"));
    }

    [Fact]
    public async Task 세션확인_백그라운드요청은_연장하지않고_만료되면끝난다()
    {
        await using var db = await fixture.BeginAsync();
        await SeedLoginUserAsync(db, "it.hong", Pwd);
        AsAnonymous(db);
        var auth = NewAuth(db);
        var login = await auth.LoginAsync(Req("it.hong"));
        await db.ExecAsync("UPDATE FW.TB_USER_SESSION SET LAST_ACCESS_DT = DATEADD(MINUTE, -10, LAST_ACCESS_DT), EXPIRE_DT = DATEADD(MINUTE, -10, EXPIRE_DT) WHERE USER_ID = 'it.hong'");
        var before = await db.ScalarAsync<string>("SELECT CONCAT(CONVERT(VARCHAR(19), LAST_ACCESS_DT, 120), '|', CONVERT(VARCHAR(19), EXPIRE_DT, 120)) FROM FW.TB_USER_SESSION WHERE USER_ID = 'it.hong'");

        var check = await auth.CheckSessionAsync(login.SessionToken, extend: false);

        Assert.Equal("it.hong", check.User!.UserId);
        Assert.Equal(before, await db.ScalarAsync<string>("SELECT CONCAT(CONVERT(VARCHAR(19), LAST_ACCESS_DT, 120), '|', CONVERT(VARCHAR(19), EXPIRE_DT, 120)) FROM FW.TB_USER_SESSION WHERE USER_ID = 'it.hong'"));
        await db.ExecAsync("UPDATE FW.TB_USER_SESSION SET EXPIRE_DT = DATEADD(SECOND, -1, SYSUTCDATETIME()) WHERE USER_ID = 'it.hong'");
        Assert.Equal(LoginResultCodes.Timeout, (await auth.CheckSessionAsync(login.SessionToken, extend: false)).EndReasonCd);
    }

    [Fact]
    public async Task 로그아웃_세션종료_로그_두번해도오류없음()
    {
        await using var db = await fixture.BeginAsync();
        await SeedLoginUserAsync(db, "it.hong", Pwd);
        AsAnonymous(db);
        var auth = NewAuth(db);
        var login = await auth.LoginAsync(Req("it.hong"));

        await auth.LogoutAsync(login.SessionToken, "IT-Agent");
        await auth.LogoutAsync(login.SessionToken, "IT-Agent");

        Assert.Equal(LoginResultCodes.Logout, (await auth.CheckSessionAsync(login.SessionToken)).EndReasonCd);
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM LOG.TB_LOGIN_LOG WHERE USER_ID = 'it.hong' AND LOGIN_RESULT_CD = 'LOGOUT'"));
    }

    [Fact]
    public async Task 로그인로그_해시체인_변조검출()
    {
        await using var db = await fixture.BeginAsync();
        var from = await db.ScalarAsync<DateTime>("SELECT SYSUTCDATETIME()");
        await SeedLoginUserAsync(db, "it.hong", Pwd);
        AsAnonymous(db);
        var auth = NewAuth(db);
        await auth.LoginAsync(Req("it.hong", "wrong"));
        await auth.LoginAsync(Req("it.hong"));
        var to = await db.ScalarAsync<DateTime>("SELECT DATEADD(SECOND, 1, SYSUTCDATETIME())");

        var ok = await db.Sp.QuerySingleAsync<VerifyResult>("LOG.USP_LOGIN_LOG_VERIFY_EXEC", new { P_FROM_DT = from, P_TO_DT = to }, TestContext.Current.CancellationToken);
        Assert.Equal(2, ok!.CheckedCnt);
        Assert.Null(ok.BrokenLogId);

        var target = await db.ScalarAsync<long>("SELECT MIN(LOG_ID) FROM LOG.TB_LOGIN_LOG WHERE USER_ID = 'it.hong'");
        await db.ExecAsync("UPDATE LOG.TB_LOGIN_LOG SET LOGIN_RESULT_CD = 'SUCCESS' WHERE LOG_ID = @ID", ("@ID", target));
        var broken = await db.Sp.QuerySingleAsync<VerifyResult>("LOG.USP_LOGIN_LOG_VERIFY_EXEC", new { P_FROM_DT = from, P_TO_DT = to }, TestContext.Current.CancellationToken);
        Assert.Equal(target, broken!.BrokenLogId);
    }

    private sealed record VerifyResult(int CheckedCnt, long? BrokenLogId);
}
