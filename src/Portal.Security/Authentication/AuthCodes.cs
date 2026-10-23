namespace Portal.Security.Authentication;

/// <summary>인증 메시지 키. 문구는 <c>FW.TB_LANG_RESOURCE</c>에 있다.</summary>
public static class AuthMessageKeys
{
    /// <summary>실패 잠금 또는 관리자 잠금.</summary>
    public const string Locked = "AUTH.LOCKED";

    /// <summary>승인 대기·퇴사·휴면·계약 종료 등 사용할 수 없는 계정.</summary>
    public const string Expired = "AUTH.EXPIRED";

    /// <summary>협력사 허용 IP 밖.</summary>
    public const string IpDenied = "AUTH.IP_DENIED";

    /// <summary>중복 로그인 거부 (정책 DENY_NEW).</summary>
    public const string ConcurrentDenied = "AUTH.CONCURRENT_DENIED";

    /// <summary>비활동 만료.</summary>
    public const string SessionExpired = "AUTH.SESSION_EXPIRED";

    /// <summary>다른 곳에서 로그인해 종료됨.</summary>
    public const string SessionKicked = "AUTH.SESSION_KICKED";

    /// <summary>비밀번호 변경 필요.</summary>
    public const string PasswordChangeRequired = "AUTH.PWD_CHANGE_REQUIRED";

    /// <summary>비밀번호 변경 완료.</summary>
    public const string PasswordChanged = "AUTH.PWD_CHANGED";

    /// <summary>2단계 인증 코드가 틀렸거나 이미 쓴 코드.</summary>
    public const string MfaInvalid = "AUTH.MFA_INVALID";

    /// <summary>2단계 인증 대기 시간이 지나 비밀번호부터 다시 입력해야 함.</summary>
    public const string MfaExpired = "AUTH.MFA_EXPIRED";

    /// <summary>이메일 인증번호가 틀렸거나 유효 시간·입력 횟수를 넘김.</summary>
    public const string OtpInvalid = "AUTH.OTP_INVALID";

    /// <summary>직전 발송 후 1분이 지나지 않아 다시 보낼 수 없음.</summary>
    public const string OtpTooSoon = "AUTH.OTP_TOO_SOON";

    /// <summary>1시간 발송 한도를 넘김.</summary>
    public const string OtpLimit = "AUTH.OTP_LIMIT";

    /// <summary>메일을 보내지 못함 (메일 설정·서버 확인).</summary>
    public const string OtpMailFailed = "AUTH.OTP_MAIL_FAILED";

    /// <summary>인증번호를 메일로 보냈다는 안내.</summary>
    public const string OtpSent = "AUTH.OTP_SENT";
}

/// <summary>로그인 결과 코드 (공통코드 <c>LOGIN_RESULT</c>). 로그인 로그와 세션 종료 사유에 쓴다.</summary>
public static class LoginResultCodes
{
    /// <summary>성공.</summary>
    public const string Success = "SUCCESS";

    /// <summary>ID 없음 또는 비밀번호 오류 (화면에는 구분하지 않는다).</summary>
    public const string FailPassword = "FAIL_PWD";

    /// <summary>잠긴 계정.</summary>
    public const string FailLocked = "FAIL_LOCKED";

    /// <summary>사용할 수 없는 계정.</summary>
    public const string FailExpired = "FAIL_EXPIRED";

    /// <summary>2단계 인증 실패.</summary>
    public const string FailMfa = "FAIL_MFA";

    /// <summary>허용되지 않은 IP.</summary>
    public const string FailIp = "FAIL_IP";

    /// <summary>중복 로그인 거부.</summary>
    public const string FailDuplicate = "FAIL_DUP";

    /// <summary>로그아웃.</summary>
    public const string Logout = "LOGOUT";

    /// <summary>다른 곳에서 로그인해 종료.</summary>
    public const string Kicked = "KICKED";

    /// <summary>비활동 만료.</summary>
    public const string Timeout = "TIMEOUT";

    /// <summary>로그인 중 계정이 잠기거나 사용 기간이 끝나 종료.</summary>
    public const string Disabled = "DISABLED";
}

/// <summary>인증 방식 코드 (<c>AUTH_METHOD_CD</c>).</summary>
public static class AuthMethods
{
    /// <summary>포털 ID/PW.</summary>
    public const string Form = "FORM";

    /// <summary>포털 ID/PW + 2단계 인증(TOTP 또는 이메일 인증번호).</summary>
    public const string FormMfa = "FORM_MFA";
}

/// <summary>인증 SP 이름.</summary>
internal static class AuthSp
{
    public const string GetUser = "FW.USP_AUTH_GET_USER";
    public const string LoginFail = "FW.USP_AUTH_LOGIN_FAIL_EXEC";
    public const string LoginSuccess = "FW.USP_AUTH_LOGIN_SUCCESS_EXEC";
    public const string SessionCheck = "FW.USP_AUTH_SESSION_CHECK_EXEC";
    public const string Logout = "FW.USP_AUTH_LOGOUT_EXEC";
    public const string PasswordHistory = "FW.USP_AUTH_PWD_HIST_GET_LIST";
    public const string PasswordChange = "FW.USP_AUTH_PWD_CHANGE_EXEC";
    public const string AdminInit = "FW.USP_AUTH_ADMIN_INIT_EXEC";
    public const string GetPermission = "FW.USP_AUTH_GET_PERMISSION";
    public const string MfaSave = "FW.USP_AUTH_MFA_SAVE_EXEC";
    public const string OtpIssue = "FW.USP_AUTH_OTP_ISSUE_EXEC";
    public const string OtpVerify = "FW.USP_AUTH_OTP_VERIFY_EXEC";

    /// <summary>중복 로그인 거부 오류 번호 (USP_AUTH_LOGIN_SUCCESS_EXEC).</summary>
    public const int ConcurrentDeniedError = 50010;

    /// <summary>이미 쓴 TOTP 단계 (USP_AUTH_LOGIN_SUCCESS_EXEC).</summary>
    public const int MfaReplayError = 50011;

    /// <summary>인증번호를 보낸 지 1분이 안 됨 (USP_AUTH_OTP_ISSUE_EXEC).</summary>
    public const int OtpTooSoonError = 52006;

    /// <summary>1시간 발송 한도 초과 (USP_AUTH_OTP_ISSUE_EXEC).</summary>
    public const int OtpLimitError = 52007;
}
