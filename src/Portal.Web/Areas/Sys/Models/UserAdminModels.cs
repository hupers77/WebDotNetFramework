using System.ComponentModel.DataAnnotations;
using Portal.Core.Keys;

namespace Portal.Web.Areas.Sys.Models;

/// <summary>사용자 관리 SP가 던지는 메시지 키.</summary>
public static class UserAdminMessageKeys
{
    /// <summary>자기 자신의 상태·역할·잠금·2단계 인증은 바꿀 수 없다.</summary>
    public const string SelfChange = "MSG.USER_SELF_CHANGE";

    /// <summary>SYS_ADMIN 역할과 SYS_ADMIN 사용자는 SYS_ADMIN만 다룬다.</summary>
    public const string SysAdminOnly = "MSG.USER_SYS_ADMIN_ONLY";

    /// <summary>사용자 회사 유형에 줄 수 없는 역할 (협력사 사용자에게 본사 역할 등).</summary>
    public const string RoleType = "MSG.USER_ROLE_TYPE";

    /// <summary>사용자와 같은 회사의 사용 중인 부서가 아니다.</summary>
    public const string DeptInvalid = "MSG.USER_DEPT_INVALID";

    /// <summary>내보낼 사용자가 너무 많다 ({0}: 최대 수).</summary>
    public const string ExportTooMany = "MSG.USER_EXPORT_TOO_MANY";
}

/// <summary>계정 초기화 종류 (<c>FW.USP_USER_ACCOUNT_RESET_EXEC</c>).</summary>
public static class UserResetActions
{
    /// <summary>비밀번호 실패 잠금 해제.</summary>
    public const string Unlock = "UNLOCK";

    /// <summary>2단계 인증(인증 앱) 등록 초기화.</summary>
    public const string MfaReset = "MFA_RESET";
}

/// <summary>사용자 목록 조건.</summary>
public sealed class UserSearch
{
    /// <summary>사용자 ID·이름 일부.</summary>
    [StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? Keyword { get; set; }

    /// <summary>회사. 비우면 전체.</summary>
    [StringLength(10, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? CompanyCd { get; set; }

    /// <summary>사용자 유형 (EMP/PARTNER/SYSTEM). 비우면 전체.</summary>
    [StringLength(10, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? UserTypeCd { get; set; }

    /// <summary>상태 (공통코드 USER_STAT). 비우면 전체.</summary>
    [StringLength(10, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? UserStatCd { get; set; }

    /// <summary>Y면 비밀번호 실패로 잠긴 계정만.</summary>
    [RegularExpression("^[YN]$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string? LockYn { get; set; }

    /// <summary>쪽 번호.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "MSG.INVALID_INPUT")]
    public int PageNo { get; set; } = 1;
}

/// <summary>사용자 목록 엑셀 내보내기 요청. 사유는 개인정보 접속기록에 남는다.</summary>
public sealed class UserExportRequest
{
    /// <summary>목록과 같은 검색 조건 (쪽 번호는 쓰지 않는다).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT")]
    public UserSearch Search { get; set; } = new();

    /// <summary>다운로드 사유.</summary>
    [Required(ErrorMessage = MessageKeys.PiiReasonRequired), StringLength(200, MinimumLength = 2, ErrorMessage = MessageKeys.PiiReasonRequired)]
    public string Reason { get; set; } = "";
}

/// <summary>사용자 (<c>FW.USP_USER_GET_LIST</c>). 이메일 등 개인정보는 담지 않는다.</summary>
public sealed class UserAdminDto
{
    /// <summary>사용자 ID.</summary>
    public string UserId { get; set; } = "";

    /// <summary>이름.</summary>
    public string UserNm { get; set; } = "";

    /// <summary>회사 코드.</summary>
    public string CompanyCd { get; set; } = "";

    /// <summary>회사명.</summary>
    public string CompanyNm { get; set; } = "";

    /// <summary>회사 유형 (HQ/PARTNER).</summary>
    public string CompanyTypeCd { get; set; } = "";

    /// <summary>부서 ID.</summary>
    public string? DeptId { get; set; }

    /// <summary>부서명.</summary>
    public string? DeptNm { get; set; }

    /// <summary>사용자 유형.</summary>
    public string UserTypeCd { get; set; } = "";

    /// <summary>사용자 유형명.</summary>
    public string UserTypeNm { get; set; } = "";

    /// <summary>상태.</summary>
    public string UserStatCd { get; set; } = "";

    /// <summary>상태명.</summary>
    public string UserStatNm { get; set; } = "";

    /// <summary>비밀번호 실패 잠금 여부.</summary>
    public string LockYn { get; set; } = "N";

    /// <summary>연속 로그인 실패 횟수.</summary>
    public int LoginFailCnt { get; set; }

    /// <summary>계정 사용 기한.</summary>
    public DateOnly? AccountEndDt { get; set; }

    /// <summary>2단계 인증(인증 앱) 등록 여부.</summary>
    public string MfaYn { get; set; } = "N";

    /// <summary>비밀번호가 있는 계정인지 (AD 전용 계정은 N).</summary>
    public string PwdYn { get; set; } = "N";

    /// <summary>마지막 로그인 (UTC).</summary>
    public DateTime? LastLoginDt { get; set; }

    /// <summary>비밀번호 변경 (UTC).</summary>
    public DateTime? PwdChgDt { get; set; }

    /// <summary>직접 부여 역할 (쉼표 구분).</summary>
    public string? RoleIds { get; set; }

    /// <summary>마지막 수정자.</summary>
    public string UpdId { get; set; } = "";

    /// <summary>마지막 수정 (UTC).</summary>
    public DateTime UpdDt { get; set; }

    /// <summary>동시성 확인용. 저장할 때 그대로 돌려보낸다.</summary>
    public byte[] RowVer { get; set; } = [];
}

/// <summary>사용자 한 명의 역할 선택지 (<c>FW.USP_USER_ROLE_GET_LIST</c>).</summary>
public sealed class UserRoleDto
{
    /// <summary>역할 ID.</summary>
    public string RoleId { get; set; } = "";

    /// <summary>역할명.</summary>
    public string RoleNm { get; set; } = "";

    /// <summary>설명.</summary>
    public string? Description { get; set; }

    /// <summary>줄 수 있는 회사 유형 (HQ/PARTNER, 비었으면 모두).</summary>
    public string? CompanyTypeCd { get; set; }

    /// <summary>직접 부여했는지.</summary>
    public string AssignedYn { get; set; } = "N";

    /// <summary>실제 적용 중인지 (부서·AD 그룹·위임으로 받은 것 포함).</summary>
    public string EffectiveYn { get; set; } = "N";

    /// <summary>지금 사용자가 이 역할을 주거나 뺄 수 있는지.</summary>
    public string AssignableYn { get; set; } = "N";

    /// <summary>직접 부여한 역할의 종료일.</summary>
    public DateOnly? EndDt { get; set; }
}

/// <summary>사용자 정보·역할 저장 요청.</summary>
public sealed class UserSaveRequest
{
    /// <summary>사용자 ID.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")]
    public string UserId { get; set; } = "";

    /// <summary>조회 때 받은 값.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), Length(8, 8, ErrorMessage = "MSG.INVALID_INPUT")]
    public byte[] RowVer { get; set; } = [];

    /// <summary>이름.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string UserNm { get; set; } = "";

    /// <summary>상태 (ACTIVE/LOCKED/DORMANT/RETIRED, 그대로 두면 지금 값).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(10, ErrorMessage = "MSG.INVALID_INPUT")]
    public string UserStatCd { get; set; } = "ACTIVE";

    /// <summary>계정 사용 기한. 비우면 기한 없음.</summary>
    public DateOnly? AccountEndDt { get; set; }

    /// <summary>부서 ID. 비우면 부서 없음. 사용자와 같은 회사의 사용 중인 부서여야 한다(지금 부서는 그대로 둘 수 있다).</summary>
    [StringLength(20, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? DeptId { get; set; }

    /// <summary>직접 부여할 역할 전체 (이 목록이 되도록 더하고 뺀다).</summary>
    [MaxLength(50, ErrorMessage = "MSG.INVALID_INPUT")]
    public List<string> RoleIds { get; set; } = [];
}

/// <summary>사용자에게 지정할 수 있는 부서 (<c>FW.USP_USER_DEPT_GET_LIST</c>, 트리 순서).</summary>
public sealed class UserDeptDto
{
    /// <summary>부서 ID.</summary>
    public string DeptId { get; set; } = "";

    /// <summary>상위 부서 ID.</summary>
    public string? ParentDeptId { get; set; }

    /// <summary>부서명.</summary>
    public string DeptNm { get; set; } = "";

    /// <summary>사용 여부 (사용 안 함은 지금 부서일 때만 온다).</summary>
    public string UseYn { get; set; } = "Y";

    /// <summary>단계 (최상위 0). 선택지 들여쓰기에 쓴다.</summary>
    public int Depth { get; set; }
}

/// <summary>잠금 해제·2단계 인증 초기화 요청.</summary>
public sealed class UserResetRequest
{
    /// <summary>사용자 ID.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")]
    public string UserId { get; set; } = "";
}

/// <summary>화면 모델: 화면 프로그램과 검색 선택지. 본사 사용자 관리와 협력사 사용자 관리가 같은 화면(Sys/Views/User/Index)을 쓴다.</summary>
/// <param name="ProgramId">화면 프로그램 ID (제목·권한 버튼).</param>
/// <param name="OwnCompanyOnly">협력사 관리자 화면이면 true: 회사·사용자 유형 검색을 숨긴다(자기 회사 협력사 사용자만 보이므로).</param>
/// <param name="Companies">(회사 코드, 회사명).</param>
/// <param name="Types">(사용자 유형, 이름).</param>
/// <param name="Stats">(상태, 이름).</param>
public sealed record UserAdminIndexModel(
    string ProgramId,
    bool OwnCompanyOnly,
    IReadOnlyList<(string Code, string Name)> Companies,
    IReadOnlyList<(string Code, string Name)> Types,
    IReadOnlyList<(string Code, string Name)> Stats);
