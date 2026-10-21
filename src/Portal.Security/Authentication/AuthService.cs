using System.Net;
using Microsoft.Extensions.Logging;
using Portal.Core.Config;
using Portal.Core.Context;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Data;
using Portal.Security.Network;
using Portal.Security.Passwords;

namespace Portal.Security.Authentication;

/// <summary>
/// ID/PW 로그인, 세션 확인, 로그아웃 (설계서 5장). 쿠키 발급과 화면 이동은 웹 계층이 한다.
/// 비밀번호가 틀렸을 때는 ID가 없는 경우와 같은 메시지·같은 계산량으로 응답해 계정 존재 여부를 숨긴다.
/// 잠김·만료·IP 차단은 비밀번호가 맞았을 때만 알려 준다.
/// </summary>
public sealed partial class AuthService(
    ISpExecutor sp,
    IPasswordHasher hasher,
    IPortalConfig config,
    ICurrentUser currentUser,
    TimeProvider time,
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

        var (token, hash) = SessionToken.Create();
        LoginSession? session;
        try
        {
            session = await sp.QuerySingleAsync<LoginSession>(
                AuthSp.LoginSuccess,
                new { P_LOGIN_USER_ID = user.UserId, P_SESSION_HASH = hash, P_AUTH_METHOD_CD = AuthMethods.Form, P_USER_AGENT = userAgent },
                cancellationToken);
        }
        catch (BizException ex) when (ex.SqlErrorNumber == AuthSp.ConcurrentDeniedError)
        {
            await FailAsync(user.UserId, LoginResultCodes.FailDuplicate, userAgent, cancellationToken);
            return LoginResult.Fail(AuthMessageKeys.ConcurrentDenied);
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
    public async Task<SessionCheckResult> CheckSessionAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (!SessionToken.IsWellFormed(token))
        {
            return new SessionCheckResult(null, null);
        }

        var row = await sp.QuerySingleAsync<SessionUser>(AuthSp.SessionCheck, new { P_SESSION_HASH = SessionToken.Hash(token!) }, cancellationToken);
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

    private Task<LoginFailResult?> FailAsync(string loginId, string resultCd, string? userAgent, CancellationToken cancellationToken) =>
        sp.QuerySingleAsync<LoginFailResult>(
            AuthSp.LoginFail,
            new { P_LOGIN_USER_ID = loginId, P_LOGIN_RESULT_CD = resultCd, P_AUTH_METHOD_CD = AuthMethods.Form, P_USER_AGENT = userAgent },
            cancellationToken);

    private static string? Truncate(string? value, int max) =>
        value is { Length: > 0 } v && v.Length > max ? v[..max] : value;

    [LoggerMessage(Level = LogLevel.Error, Message = "회사 {CompanyCd}의 허용 IP 목록 형식이 잘못되어 로그인을 막았습니다")]
    private static partial void LogBadAllowList(ILogger logger, string companyCd, Exception ex);
}
