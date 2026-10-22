namespace Portal.Core.Keys;

/// <summary>
/// <c>FW.TB_CONFIG</c> 설정 키. 코드에서는 이 상수로만 설정을 읽고, 기본값은 초기 데이터 스크립트(<c>db/seed</c>)에 둔다.
/// 키를 추가하면 초기 데이터 정의서 6장과 시드 스크립트도 같은 변경에서 고친다.
/// </summary>
public static class ConfigKeys
{
    /// <summary>인증 모드: FORM / AD / BOTH.</summary>
    public const string AuthMode = "AUTH.MODE";

    /// <summary>AD 로그인 시 포털 계정 자동 생성.</summary>
    public const string AuthAdAutoProvision = "AUTH.AD_AUTO_PROVISION";

    /// <summary>협력사 2단계 인증.</summary>
    public const string AuthPartnerMfa = "AUTH.PARTNER_MFA";

    /// <summary>관리자 역할 2단계 인증.</summary>
    public const string AuthAdminMfa = "AUTH.ADMIN_MFA";

    /// <summary>IP당 분당 로그인 시도 한도.</summary>
    public const string AuthLoginRatePerMin = "AUTH.LOGIN_RATE_PER_MIN";

    /// <summary>3종류 조합 최소 길이.</summary>
    public const string PwdMinLen3types = "PWD.MIN_LEN_3TYPES";

    /// <summary>2종류 조합 최소 길이.</summary>
    public const string PwdMinLen2types = "PWD.MIN_LEN_2TYPES";

    /// <summary>재사용 금지 이력 수.</summary>
    public const string PwdHistoryCount = "PWD.HISTORY_COUNT";

    /// <summary>비밀번호 변경 주기(일).</summary>
    public const string PwdExpireDays = "PWD.EXPIRE_DAYS";

    /// <summary>실패 잠금 횟수.</summary>
    public const string PwdLockCount = "PWD.LOCK_COUNT";

    /// <summary>잠금 자동 해제 시간(분, 0 = 관리자 해제만).</summary>
    public const string PwdLockMinutes = "PWD.LOCK_MINUTES";

    /// <summary>세션 비활동 만료(분).</summary>
    public const string SessionIdleMinutes = "SESSION.IDLE_MINUTES";

    /// <summary>중복 로그인 정책: KICK_OLD / DENY_NEW / ALLOW.</summary>
    public const string SessionConcurrentPolicy = "SESSION.CONCURRENT_POLICY";

    /// <summary>미접속 휴면 전환(일).</summary>
    public const string UserDormantDays = "USER.DORMANT_DAYS";

    /// <summary>파일 1개 최대 크기(MB).</summary>
    public const string FileMaxSizeMb = "FILE.MAX_SIZE_MB";

    /// <summary>첨부 묶음 하나의 최대 파일 수.</summary>
    public const string FileMaxCount = "FILE.MAX_COUNT";

    /// <summary>멀티 탭 사용.</summary>
    public const string UiTabMode = "UI.TAB_MODE";

    /// <summary>최대 탭 수.</summary>
    public const string UiMaxTabs = "UI.MAX_TABS";

    /// <summary>그리드 기본 행 수.</summary>
    public const string UiPageSize = "UI.PAGE_SIZE";

    /// <summary>기본 언어.</summary>
    public const string UiDefaultLang = "UI.DEFAULT_LANG";

    /// <summary>사이트명.</summary>
    public const string SiteName = "SITE.NAME";

    /// <summary>브랜드 색상.</summary>
    public const string SiteThemeColor = "SITE.THEME_COLOR";

    /// <summary>포털 주소 (메일 본문의 링크, 예: https://portal.example.com).</summary>
    public const string SiteBaseUrl = "SITE.BASE_URL";

    /// <summary>메일 실제 발송 여부.</summary>
    public const string MailEnabled = "MAIL.ENABLED";

    /// <summary>SMTP 서버.</summary>
    public const string MailSmtpHost = "MAIL.SMTP_HOST";

    /// <summary>SMTP 포트.</summary>
    public const string MailSmtpPort = "MAIL.SMTP_PORT";

    /// <summary>SMTP 계정. 비어 있으면 인증 없이 보낸다(사내 릴레이).</summary>
    public const string MailSmtpUser = "MAIL.SMTP_USER";

    /// <summary>SMTP 암호화(STARTTLS) 사용.</summary>
    public const string MailSmtpSsl = "MAIL.SMTP_SSL";

    /// <summary>SMTP 계정 비밀번호(SECRET).</summary>
    public const string MailSmtpPassword = "MAIL.SMTP_PASSWORD";

    /// <summary>보내는 주소.</summary>
    public const string MailFrom = "MAIL.FROM";

    /// <summary>접속기록·감사 로그 보관 기간(년).</summary>
    public const string LogRetentionYears = "LOG.RETENTION_YEARS";

    /// <summary>오류 로그 보관 기간(일).</summary>
    public const string LogErrorRetentionDays = "LOG.ERROR_RETENTION_DAYS";

    /// <summary>느린 SP 기준(ms).</summary>
    public const string LogSlowSpMs = "LOG.SLOW_SP_MS";

    /// <summary>SP 명령 타임아웃(초, 재시작 필요).</summary>
    public const string SpCommandTimeoutSec = "SP.COMMAND_TIMEOUT_SEC";

    /// <summary>동시 PDF 변환 수(재시작 필요).</summary>
    public const string PdfMaxConcurrency = "PDF.MAX_CONCURRENCY";

    /// <summary>Edge 실행 파일 경로.</summary>
    public const string PdfEdgePath = "PDF.EDGE_PATH";

    /// <summary>외부 API 토큰 유효 시간(분).</summary>
    public const string ApiTokenMinutes = "API.TOKEN_MINUTES";

    /// <summary>API 클라이언트 기본 호출 한도.</summary>
    public const string ApiDefaultRatePerMin = "API.DEFAULT_RATE_PER_MIN";

    /// <summary>LDAPS 서버.</summary>
    public const string AdLdapServer = "AD.LDAP_SERVER";

    /// <summary>동기화 대상 OU.</summary>
    public const string AdBaseDn = "AD.BASE_DN";

    /// <summary>동기화 계정 비밀번호(SECRET).</summary>
    public const string AdBindPassword = "AD.BIND_PASSWORD";

    /// <summary>전자결재 연동 구현 이름(재시작 필요).</summary>
    public const string ApprovalGateway = "APPROVAL.GATEWAY";

    /// <summary>알림 확인 주기(초).</summary>
    public const string NotifyPollSeconds = "NOTIFY.POLL_SECONDS";

    /// <summary>점검 모드: Y면 관리자만 접속.</summary>
    public const string SysMaintenanceMode = "SYS.MAINTENANCE_MODE";
}
