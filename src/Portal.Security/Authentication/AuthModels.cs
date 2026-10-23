namespace Portal.Security.Authentication;

/// <summary>ID/PW 로그인 요청.</summary>
/// <param name="UserId">입력한 사용자 ID.</param>
/// <param name="Password">입력한 비밀번호. 기록하지 않는다.</param>
/// <param name="UserAgent">브라우저 정보 (300자까지 기록).</param>
public sealed record LoginRequest(string UserId, string Password, string? UserAgent);

/// <summary>로그인 결과.</summary>
/// <param name="Succeeded">성공 여부.</param>
/// <param name="MessageKey">실패 시 화면에 보여 줄 메시지 키.</param>
/// <param name="SessionToken">성공 시 쿠키에 담을 세션 토큰 원문.</param>
/// <param name="UserId">성공 시 DB에 저장된 사용자 ID (대소문자 정규화).</param>
/// <param name="PasswordChangeRequired">최초 로그인·초기화·만료로 비밀번호 변경 화면으로 보내야 하는지.</param>
/// <param name="ExpireDt">세션 만료 시각 (UTC).</param>
/// <param name="MfaRequired">비밀번호는 맞았고 2단계 인증 코드가 필요하다 (세션은 아직 없다).</param>
/// <param name="MfaEnrolled">인증 앱을 이미 등록했는지. false면 등록부터 한다.</param>
public sealed record LoginResult(
    bool Succeeded,
    string? MessageKey,
    string? SessionToken = null,
    string? UserId = null,
    bool PasswordChangeRequired = false,
    DateTime? ExpireDt = null,
    bool MfaRequired = false,
    bool MfaEnrolled = false)
{
    internal static LoginResult Fail(string messageKey) => new(false, messageKey);
}

/// <summary>2단계 인증 코드 확인 요청.</summary>
/// <param name="Ticket">비밀번호 확인 뒤 받은 대기표.</param>
/// <param name="Code">인증 앱 또는 이메일로 받은 6자리 코드.</param>
/// <param name="UserAgent">브라우저 정보.</param>
/// <param name="Email">true면 이메일 인증번호로 확인한다 (인증 앱 코드가 아니라).</param>
public sealed record MfaVerifyRequest(Mfa.MfaTicket Ticket, string? Code, string? UserAgent, bool Email = false);

/// <summary>이메일 인증번호 발송 결과.</summary>
/// <param name="Sent">보냈는지.</param>
/// <param name="MessageKey">보냈으면 안내, 못 보냈으면 사유 메시지 키.</param>
public sealed record MfaEmailSendResult(bool Sent, string MessageKey);

/// <summary>세션 확인 결과. <see cref="User"/>가 null이면 로그아웃 상태이고 <see cref="EndReasonCd"/>가 사유다.</summary>
/// <param name="User">로그인 사용자.</param>
/// <param name="EndReasonCd">종료 사유 (<see cref="LoginResultCodes"/>). 알 수 없는 세션이면 null.</param>
public sealed record SessionCheckResult(SessionUser? User, string? EndReasonCd)
{
    /// <summary>종료 사유에 맞는 안내 메시지 키. 안내가 필요 없으면 null.</summary>
    public string? EndMessageKey => EndReasonCd switch
    {
        LoginResultCodes.Kicked => AuthMessageKeys.SessionKicked,
        LoginResultCodes.Timeout => AuthMessageKeys.SessionExpired,
        LoginResultCodes.Disabled => AuthMessageKeys.Expired,
        _ => null,
    };
}

/// <summary>세션의 사용자 정보 (<c>USP_AUTH_SESSION_CHECK_EXEC</c> 결과).</summary>
public sealed class SessionUser
{
    /// <summary>사용자 ID.</summary>
    public string UserId { get; set; } = "";

    /// <summary>회사 코드.</summary>
    public string CompanyCd { get; set; } = "";

    /// <summary>부서 ID.</summary>
    public string? DeptId { get; set; }

    /// <summary>사용자 유형 (EMP/PARTNER/SYSTEM).</summary>
    public string UserTypeCd { get; set; } = "";

    /// <summary>이름.</summary>
    public string UserNm { get; set; } = "";

    /// <summary>언어.</summary>
    public string LangCd { get; set; } = "ko";

    /// <summary>시간대 ID.</summary>
    public string TimezoneId { get; set; } = "";

    /// <summary>비밀번호 변경 강제 표시.</summary>
    public string PwdChgReqYn { get; set; } = "N";

    /// <summary>마지막 비밀번호 변경 시각 (UTC).</summary>
    public DateTime? PwdChgDt { get; set; }

    /// <summary>세션 만료 시각 (UTC).</summary>
    public DateTime ExpireDt { get; set; }

    /// <summary>종료 사유 (살아 있는 세션이면 null).</summary>
    public string? EndReasonCd { get; set; }
}

/// <summary><c>USP_AUTH_GET_USER</c> 결과.</summary>
internal sealed class AuthUser
{
    public string UserId { get; set; } = "";

    public string CompanyCd { get; set; } = "";

    public string UserTypeCd { get; set; } = "";

    public string LangCd { get; set; } = "";

    public byte[]? PwdHash { get; set; }

    public byte[]? PwdSalt { get; set; }

    public DateTime? PwdChgDt { get; set; }

    public string PwdChgReqYn { get; set; } = "N";

    public string? AllowIpList { get; set; }

    public string AccountStat { get; set; } = "";

    public byte[]? MfaSecretEnc { get; set; }

    public long? MfaLastStep { get; set; }

    public string AdminYn { get; set; } = "N";

    public string HasEmail { get; set; } = "N";
}

/// <summary><c>USP_AUTH_OTP_ISSUE_EXEC</c> 결과.</summary>
internal sealed class OtpIssued
{
    public byte[]? EmailEnc { get; set; }

    public string LangCd { get; set; } = "ko";
}

/// <summary><c>USP_AUTH_OTP_VERIFY_EXEC</c> 결과.</summary>
internal sealed class OtpVerified
{
    public string MatchYn { get; set; } = "N";
}

/// <summary><c>USP_AUTH_LOGIN_SUCCESS_EXEC</c> 결과.</summary>
internal sealed class LoginSession
{
    public DateTime ExpireDt { get; set; }

    public int KickedCnt { get; set; }
}

/// <summary><c>USP_AUTH_LOGIN_FAIL_EXEC</c> 결과.</summary>
internal sealed class LoginFailResult
{
    public string LockYn { get; set; } = "N";
}

/// <summary><c>USP_AUTH_PWD_HIST_GET_LIST</c> 결과 행.</summary>
internal sealed class PasswordHistoryRow
{
    public int SeqNo { get; set; }

    public byte[] PwdHash { get; set; } = [];

    public byte[] PwdSalt { get; set; } = [];
}

/// <summary><c>USP_AUTH_ADMIN_INIT_EXEC</c> 결과.</summary>
internal sealed class AdminInitResult
{
    public string CreatedYn { get; set; } = "N";
}
