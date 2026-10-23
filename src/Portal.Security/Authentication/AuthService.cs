using System.Net;
using Microsoft.Extensions.Logging;
using Portal.Core.Config;
using Portal.Core.Context;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Data;
using Portal.Security.Mfa;
using Portal.Security.Network;
using Portal.Security.Passwords;

namespace Portal.Security.Authentication;

/// <summary>
/// ID/PW 로그인, 세션 확인, 로그아웃 (설계서 5장). 쿠키 발급과 화면 이동은 웹 계층이 한다.
/// 비밀번호가 틀렸을 때는 ID가 없는 경우와 같은 메시지·같은 계산량으로 응답해 계정 존재 여부를 숨긴다.
/// 잠김·만료·IP 차단은 비밀번호가 맞았을 때만 알려 준다.
/// 2단계 인증 대상(협력사 <c>AUTH.PARTNER_MFA</c>, 관리자 역할 <c>AUTH.ADMIN_MFA</c>)은 비밀번호가 맞아도 세션을 만들지 않고
/// <see cref="LoginResult.MfaRequired"/>를 돌려준다. 코드를 확인하면(<see cref="VerifyMfaAsync"/>) 세션을 만든다.
/// 틀린 코드도 로그인 실패로 세어 같은 횟수에서 잠근다.
/// 이메일 인증번호를 허용하는 대상(<c>AUTH.PARTNER_EMAIL_OTP</c>, <c>AUTH.ADMIN_EMAIL_OTP</c>, 이메일 등록)은 인증 앱 대신
/// 메일로 받은 6자리 번호로도 확인할 수 있다(<see cref="SendMfaEmailAsync"/>, <see cref="MfaVerifyRequest.Email"/>).
/// </summary>
public sealed partial class AuthService(
    ISpExecutor sp,
    IPasswordHasher hasher,
    IPortalConfig config,
    ICurrentUser currentUser,
    TimeProvider time,
    IMfaSecretProtector mfaSecrets,
    IMfaCodeMailer otpMailer,
    ILogger<AuthService> logger)
{
    private const int MaxUserIdLength = 30;
    private const int MaxUserAgentLength = 300;

    // ID가 없을 때도 같은 시간이 걸리도록 비교에 쓰는 값
    private static readonly byte[] DummyHash = new byte[Pbkdf2PasswordHasher.HashSize];
    private static readonly byte[] DummySalt = new byte[Pbkdf2PasswordHasher.SaltSize];

    /// <summary>ID/PW 로그인. 성공하면 새 세션을 만든다(세션 고정 방지: 항상 새 토큰).</summary>
    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var loginId = request.UserId.Trim();
        var userAgent = Truncate(request.UserAgent, MaxUserAgentLength);
        if (loginId.Length == 0 || request.Password.Length == 0 || request.Password.Length > PasswordPolicy.MaxLength)
        {
            return LoginResult.Fail(MessageKeys.LoginFailed);
        }

        loginId = Truncate(loginId, MaxUserIdLength)!;
        var user = await sp.QuerySingleAsync<AuthUser>(AuthSp.GetUser, new { P_LOGIN_USER_ID = loginId }, cancellationToken);

        bool passwordOk;
        if (user is { PwdHash: { } pwdHash, PwdSalt: { } pwdSalt })
        {
            passwordOk = hasher.Verify(request.Password, pwdHash, pwdSalt);
        }
        else
        {
            // 없는 ID, AD 전용 계정: 결과는 버리고 계산만 한다
            _ = hasher.Verify(request.Password, DummyHash, DummySalt);
            passwordOk = false;
        }

        if (user is null || !passwordOk)
        {
            await FailAsync(user?.UserId ?? loginId, LoginResultCodes.FailPassword, userAgent, cancellationToken);
            return LoginResult.Fail(MessageKeys.LoginFailed);
        }

        var denied = Deny(user);
        if (denied is { } d)
        {
            await FailAsync(user.UserId, d.ResultCd, userAgent, cancellationToken);
            return LoginResult.Fail(d.MessageKey);
        }

        if (RequiresMfa(user))
        {
            return new LoginResult(false, null, UserId: user.UserId, MfaRequired: true, MfaEnrolled: user.MfaSecretEnc is not null);
        }

        return await CreateSessionAsync(user, AuthMethods.Form, null, userAgent, cancellationToken);
    }

    /// <summary>
    /// 2단계 인증 코드를 확인하고 세션을 만든다. 등록 중이면(대기표에 새 비밀키) 코드가 맞을 때 비밀키를 저장한다.
    /// 계정 상태(잠김·만료·IP)는 비밀번호 확인 뒤 바뀌었을 수 있어 다시 확인한다.
    /// </summary>
    public async Task<LoginResult> VerifyMfaAsync(MfaVerifyRequest request, CancellationToken cancellationToken = default)
    {
        var userAgent = Truncate(request.UserAgent, MaxUserAgentLength);
        var user = await sp.QuerySingleAsync<AuthUser>(AuthSp.GetUser, new { P_LOGIN_USER_ID = request.Ticket.UserId }, cancellationToken);
        if (user is null)
        {
            return LoginResult.Fail(AuthMessageKeys.MfaExpired);
        }

        if (Deny(user) is { } denied)
        {
            await FailAsync(user.UserId, denied.ResultCd, userAgent, cancellationToken);
            return LoginResult.Fail(denied.MessageKey);
        }

        if (request.Email)
        {
            return await VerifyEmailOtpAsync(user, request.Code, userAgent, cancellationToken);
        }

        var enrolling = user.MfaSecretEnc is null;
        byte[]? secret;
        try
        {
            secret = enrolling ? request.Ticket.PendingSecret : mfaSecrets.Unprotect(user.MfaSecretEnc!);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            // 키 폴더를 잃었거나 바뀌었다: 관리자가 2단계 인증을 초기화해야 다시 등록할 수 있다
            LogBadMfaSecret(logger, user.UserId, ex);
            return LoginResult.Fail(AuthMessageKeys.MfaExpired);
        }

        if (secret is not { Length: Totp.SecretSize })
        {
            return LoginResult.Fail(AuthMessageKeys.MfaExpired);
        }

        var step = Totp.Verify(secret, request.Code, time.GetUtcNow(), user.MfaLastStep);
        if (step is null)
        {
            var failed = await FailAsync(user.UserId, LoginResultCodes.FailMfa, userAgent, cancellationToken, AuthMethods.FormMfa);
            return LoginResult.Fail(failed?.LockYn == "Y" ? AuthMessageKeys.Locked : AuthMessageKeys.MfaInvalid);
        }

        if (enrolling)
        {
            try
            {
                await sp.ExecuteAsync(
                    AuthSp.MfaSave,
                    new { P_LOGIN_USER_ID = user.UserId, P_MFA_SECRET_ENC = mfaSecrets.Protect(secret), P_MFA_STEP = step },
                    cancellationToken);
            }
            catch (BizException ex) when (ex.MessageKey == MessageKeys.Concurrency)
            {
                // 다른 창에서 먼저 등록했다: 등록한 키의 코드로 다시 입력해야 한다
                return LoginResult.Fail(AuthMessageKeys.MfaExpired);
            }
        }

        return await CreateSessionAsync(user, AuthMethods.FormMfa, enrolling ? null : step, userAgent, cancellationToken);
    }

    /// <summary>이메일 인증번호를 쓸 수 있는 사용자인지 (화면에 [이메일로 받기]를 보일지).</summary>
    public async Task<bool> CanUseEmailOtpAsync(string userId, CancellationToken cancellationToken = default) =>
        await sp.QuerySingleAsync<AuthUser>(AuthSp.GetUser, new { P_LOGIN_USER_ID = userId }, cancellationToken) is { } user
        && AllowsEmailOtp(user);

    /// <summary>
    /// 이메일 인증번호를 만들어 메일로 보낸다. 제한(60초 간격, 1시간 5통)을 넘으면 보내지 않고 사유를 돌려준다.
    /// 번호는 해시만 DB에 두고 원문은 메일로만 나간다. 메일을 못 보내면 번호를 쓸 수 없지만 1분 뒤 다시 보낼 수 있다.
    /// </summary>
    public async Task<MfaEmailSendResult> SendMfaEmailAsync(Mfa.MfaTicket ticket, CancellationToken cancellationToken = default)
    {
        var user = await sp.QuerySingleAsync<AuthUser>(AuthSp.GetUser, new { P_LOGIN_USER_ID = ticket.UserId }, cancellationToken);
        if (user is null || Deny(user) is not null)
        {
            return new MfaEmailSendResult(false, AuthMessageKeys.MfaExpired);
        }

        if (!AllowsEmailOtp(user))
        {
            return new MfaEmailSendResult(false, AuthMessageKeys.OtpMailFailed);
        }

        var code = EmailOtp.NewCode();
        OtpIssued? issued;
        try
        {
            issued = await sp.QuerySingleAsync<OtpIssued>(
                AuthSp.OtpIssue,
                new { P_LOGIN_USER_ID = user.UserId, P_CODE_HASH = EmailOtp.Hash(user.UserId, code) },
                cancellationToken);
        }
        catch (BizException ex) when (ex.SqlErrorNumber == AuthSp.OtpTooSoonError)
        {
            return new MfaEmailSendResult(false, AuthMessageKeys.OtpTooSoon);
        }
        catch (BizException ex) when (ex.SqlErrorNumber == AuthSp.OtpLimitError)
        {
            return new MfaEmailSendResult(false, AuthMessageKeys.OtpLimit);
        }

        try
        {
            await otpMailer.SendAsync(issued!.EmailEnc!, issued.LangCd, code, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogOtpMailFailed(logger, user.UserId, ex.GetType().Name);
            return new MfaEmailSendResult(false, AuthMessageKeys.OtpMailFailed);
        }

        return new MfaEmailSendResult(true, AuthMessageKeys.OtpSent);
    }

    private async Task<LoginResult> VerifyEmailOtpAsync(AuthUser user, string? code, string? userAgent, CancellationToken cancellationToken)
    {
        if (!AllowsEmailOtp(user))
        {
            return LoginResult.Fail(AuthMessageKeys.MfaExpired);
        }

        var matched = EmailOtp.IsWellFormed(code)
            && (await sp.QuerySingleAsync<OtpVerified>(
                AuthSp.OtpVerify,
                new { P_LOGIN_USER_ID = user.UserId, P_CODE_HASH = EmailOtp.Hash(user.UserId, code!) },
                cancellationToken))?.MatchYn == "Y";
        if (!matched)
        {
            var failed = await FailAsync(user.UserId, LoginResultCodes.FailMfa, userAgent, cancellationToken, AuthMethods.FormMfa);
            return LoginResult.Fail(failed?.LockYn == "Y" ? AuthMessageKeys.Locked : AuthMessageKeys.OtpInvalid);
        }

        // 인증 앱을 등록하지 않아도 되고, 등록된 TOTP 단계(MFA_LAST_STEP)는 건드리지 않는다
        return await CreateSessionAsync(user, AuthMethods.FormMfa, null, userAgent, cancellationToken);
    }

    /// <summary>
    /// 이메일 인증번호 허용 여부: 2단계 인증 대상이면서 이메일이 있고, 해당 구분의 설정을 켠 경우
    /// (협력사 <c>AUTH.PARTNER_EMAIL_OTP</c>, 관리자 역할 <c>AUTH.ADMIN_EMAIL_OTP</c>).
    /// </summary>
    private bool AllowsEmailOtp(AuthUser user) =>
        user.HasEmail == "Y"
        && ((user.UserTypeCd == "PARTNER" && config.GetBool(ConfigKeys.AuthPartnerMfa) && config.GetBool(ConfigKeys.AuthPartnerEmailOtp))
            || (user.AdminYn == "Y" && config.GetBool(ConfigKeys.AuthAdminMfa) && config.GetBool(ConfigKeys.AuthAdminEmailOtp)));

    /// <summary>2단계 인증 대상인지: 협력사(<c>AUTH.PARTNER_MFA</c>) 또는 관리자 역할(<c>AUTH.ADMIN_MFA</c>).</summary>
    private bool RequiresMfa(AuthUser user) =>
        (user.UserTypeCd == "PARTNER" && config.GetBool(ConfigKeys.AuthPartnerMfa))
        || (user.AdminYn == "Y" && config.GetBool(ConfigKeys.AuthAdminMfa));

    private async Task<LoginResult> CreateSessionAsync(AuthUser user, string authMethod, long? mfaStep, string? userAgent, CancellationToken cancellationToken)
    {
        var (token, hash) = SessionToken.Create();
        LoginSession? session;
        try
        {
            session = await sp.QuerySingleAsync<LoginSession>(
                AuthSp.LoginSuccess,
                new { P_LOGIN_USER_ID = user.UserId, P_SESSION_HASH = hash, P_AUTH_METHOD_CD = authMethod, P_USER_AGENT = userAgent, P_MFA_STEP = mfaStep },
                cancellationToken);
        }
        catch (BizException ex) when (ex.SqlErrorNumber == AuthSp.ConcurrentDeniedError)
        {
            await FailAsync(user.UserId, LoginResultCodes.FailDuplicate, userAgent, cancellationToken);
            return LoginResult.Fail(AuthMessageKeys.ConcurrentDenied);
        }
        catch (BizException ex) when (ex.SqlErrorNumber == AuthSp.MfaReplayError)
        {
            // 같은 코드가 동시에 두 번 들어왔다
            await FailAsync(user.UserId, LoginResultCodes.FailMfa, userAgent, cancellationToken, AuthMethods.FormMfa);
            return LoginResult.Fail(AuthMessageKeys.MfaInvalid);
        }

        return new LoginResult(
            true,
            null,
            token,
            user.UserId,
            IsPasswordChangeRequired(user.PwdChgReqYn, user.PwdChgDt),
            session!.ExpireDt);
    }

    /// <summary>요청마다 세션을 확인하고 비활동 만료 시각을 연장한다.</summary>
    public Task<SessionCheckResult> CheckSessionAsync(string? token, CancellationToken cancellationToken = default) =>
        CheckSessionAsync(token, extend: true, cancellationToken);

    /// <summary>
    /// 세션 확인. <paramref name="extend"/>가 false면 비활동 만료 시각을 늘리지 않는다
    /// (헤더 알림 배지처럼 사용자가 하지 않은 백그라운드 요청이 자동 로그아웃을 막지 않도록).
    /// </summary>
    public async Task<SessionCheckResult> CheckSessionAsync(string? token, bool extend, CancellationToken cancellationToken = default)
    {
        if (!SessionToken.IsWellFormed(token))
        {
            return new SessionCheckResult(null, null);
        }

        var row = await sp.QuerySingleAsync<SessionUser>(
            AuthSp.SessionCheck,
            new { P_SESSION_HASH = SessionToken.Hash(token!), P_TOUCH_YN = extend ? "Y" : "N" },
            cancellationToken);
        return row switch
        {
            null => new SessionCheckResult(null, null),
            { EndReasonCd: not null } => new SessionCheckResult(null, row.EndReasonCd),
            _ => new SessionCheckResult(row, null),
        };
    }

    /// <summary>로그아웃. 이미 끝난 세션이어도 오류 없이 끝난다.</summary>
    public async Task LogoutAsync(string? token, string? userAgent, CancellationToken cancellationToken = default)
    {
        if (!SessionToken.IsWellFormed(token))
        {
            return;
        }

        await sp.ExecuteAsync(
            AuthSp.Logout,
            new { P_SESSION_HASH = SessionToken.Hash(token!), P_USER_AGENT = Truncate(userAgent, MaxUserAgentLength) },
            cancellationToken);
    }

    /// <summary>비밀번호 변경이 필요한지: 강제 표시, 변경 이력 없음, 변경 주기(<c>PWD.EXPIRE_DAYS</c>, 0이면 만료 없음) 경과.</summary>
    public bool IsPasswordChangeRequired(string pwdChgReqYn, DateTime? pwdChgDt)
    {
        if (pwdChgReqYn == "Y" || pwdChgDt is null)
        {
            return true;
        }

        var expireDays = config.GetInt(ConfigKeys.PwdExpireDays);
        var changedUtc = DateTime.SpecifyKind(pwdChgDt.Value, DateTimeKind.Utc);
        return expireDays > 0 && changedUtc.AddDays(expireDays) < time.GetUtcNow().UtcDateTime;
    }

    private (string ResultCd, string MessageKey)? Deny(AuthUser user)
    {
        switch (user.AccountStat)
        {
            case "OK":
                break;
            case "LOCKED":
                return (LoginResultCodes.FailLocked, AuthMessageKeys.Locked);
            default:
                return (LoginResultCodes.FailExpired, AuthMessageKeys.Expired);
        }

        try
        {
            var ip = IPAddress.TryParse(currentUser.ClientIp, out var parsed) ? parsed : null;
            return IpAllowList.Parse(user.AllowIpList).Allows(ip) ? null : (LoginResultCodes.FailIp, AuthMessageKeys.IpDenied);
        }
        catch (FormatException ex)
        {
            // 목록이 잘못되었으면 모두 허용하지 않고 막는다(관리자가 회사 관리 화면에서 고친다)
            LogBadAllowList(logger, user.CompanyCd, ex);
            return (LoginResultCodes.FailIp, AuthMessageKeys.IpDenied);
        }
    }

    private Task<LoginFailResult?> FailAsync(string loginId, string resultCd, string? userAgent, CancellationToken cancellationToken, string authMethod = AuthMethods.Form) =>
        sp.QuerySingleAsync<LoginFailResult>(
            AuthSp.LoginFail,
            new { P_LOGIN_USER_ID = loginId, P_LOGIN_RESULT_CD = resultCd, P_AUTH_METHOD_CD = authMethod, P_USER_AGENT = userAgent },
            cancellationToken);

    private static string? Truncate(string? value, int max) =>
        value is { Length: > 0 } v && v.Length > max ? v[..max] : value;

    [LoggerMessage(Level = LogLevel.Error, Message = "회사 {CompanyCd}의 허용 IP 목록 형식이 잘못되어 로그인을 막았습니다")]
    private static partial void LogBadAllowList(ILogger logger, string companyCd, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "사용자 {UserId}에게 이메일 인증번호를 보내지 못했습니다: {Error}")]
    private static partial void LogOtpMailFailed(ILogger logger, string userId, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "사용자 {UserId}의 2단계 인증 비밀키를 풀 수 없습니다 (Data Protection 키 확인, 관리자가 초기화하면 다시 등록)")]
    private static partial void LogBadMfaSecret(ILogger logger, string userId, Exception ex);
}
