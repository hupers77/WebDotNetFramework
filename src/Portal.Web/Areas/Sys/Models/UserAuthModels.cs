using System.ComponentModel.DataAnnotations;

namespace Portal.Web.Areas.Sys.Models;

/// <summary>사용자별 예외 권한 SP가 던지는 메시지 키.</summary>
public static class UserAuthMessageKeys
{
    /// <summary>자기 예외 권한은 시스템 관리자만 고친다.</summary>
    public const string Self = "MSG.USER_AUTH_SELF";

    /// <summary>시스템 관리자는 모든 권한을 가지므로 예외 권한을 두지 않는다.</summary>
    public const string SysAdmin = "MSG.USER_AUTH_SYS_ADMIN";

    /// <summary>새로 넣거나 바꾼 예외 권한의 종료일이 지났다.</summary>
    public const string EndPast = "MSG.USER_AUTH_END_PAST";

    /// <summary>대상의 회사 유형에 열리지 않은 화면은 추가 허용할 수 없다.</summary>
    public const string Scope = "MSG.USER_AUTH_SCOPE";
}

/// <summary>예외 권한 대상 사용자 (<c>FW.USP_USER_AUTH_GET_LIST</c> 결과 1).</summary>
public sealed class UserAuthTarget
{
    /// <summary>사용자 ID.</summary>
    public string UserId { get; set; } = "";

    /// <summary>이름.</summary>
    public string UserNm { get; set; } = "";

    /// <summary>회사명.</summary>
    public string CompanyNm { get; set; } = "";

    /// <summary>회사 유형 (HQ/PARTNER).</summary>
    public string CompanyTypeCd { get; set; } = "";

    /// <summary>계정 상태.</summary>
    public string UserStatCd { get; set; } = "";

    /// <summary>대상이 시스템 관리자 (예외 권한을 두지 않는다).</summary>
    public string SysAdminYn { get; set; } = "N";

    /// <summary>대상이 나.</summary>
    public string SelfYn { get; set; } = "N";

    /// <summary>동시성 확인용 (예외 권한 전체의 해시).</summary>
    public byte[] Version { get; set; } = [];
}

/// <summary>예외 권한 원천 행 (<c>FW.USP_USER_AUTH_GET_LIST</c> 결과 2, 프로그램 × 기능).</summary>
public sealed class UserAuthRow
{
    /// <summary>프로그램 ID.</summary>
    public string ProgramId { get; set; } = "";

    /// <summary>관리용 이름 (메뉴가 없을 때 쓴다).</summary>
    public string ProgramNm { get; set; } = "";

    /// <summary>공개 대상 (ALL/EMP/PARTNER).</summary>
    public string OpenScopeCd { get; set; } = "";

    /// <summary>대상 회사 유형에 열린 화면 (추가 허용 가능).</summary>
    public string GrantableYn { get; set; } = "N";

    /// <summary>기능 코드.</summary>
    public string FuncCd { get; set; } = "";

    /// <summary>대상의 유효 역할이 이미 허용.</summary>
    public string RoleYn { get; set; } = "N";

    /// <summary>예외 유형 (GRANT/DENY). 없으면 null.</summary>
    public string? GrantType { get; set; }

    /// <summary>예외 종료일.</summary>
    public DateTime? EndDt { get; set; }
}

/// <summary>사용자 한 명의 예외 권한 화면 데이터.</summary>
/// <param name="Target">대상 사용자.</param>
/// <param name="Programs">사용 중인 프로그램과 기능별 상태.</param>
public sealed record UserAuthDetail(UserAuthTarget Target, IReadOnlyList<UserAuthProgram> Programs);

/// <summary>프로그램 하나 (화면 제목은 메뉴명).</summary>
/// <param name="ProgramId">프로그램 ID.</param>
/// <param name="ProgramNm">화면 이름.</param>
/// <param name="Grantable">추가 허용 가능.</param>
/// <param name="Funcs">기능별 상태.</param>
public sealed record UserAuthProgram(string ProgramId, string ProgramNm, bool Grantable, IReadOnlyList<UserAuthFuncState> Funcs);

/// <summary>기능 하나의 역할 허용 여부와 예외.</summary>
/// <param name="FuncCd">기능 코드.</param>
/// <param name="ByRole">역할로 이미 허용.</param>
/// <param name="GrantType">예외 유형 (GRANT/DENY). 없으면 null.</param>
/// <param name="EndDt">예외 종료일 (yyyy-MM-dd).</param>
public sealed record UserAuthFuncState(string FuncCd, bool ByRole, string? GrantType, string? EndDt);

/// <summary>사용자 한 명의 예외 권한 저장 요청 (화면의 목록 전부).</summary>
public sealed class UserAuthSaveRequest
{
    /// <summary>대상 사용자 ID.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")]
    public string TargetUserId { get; set; } = "";

    /// <summary>조회 때 받은 값.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), Length(32, 32, ErrorMessage = "MSG.INVALID_INPUT")]
    public byte[] Version { get; set; } = [];

    /// <summary>예외 권한 전부 (사용 중인 프로그램 기준).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), MaxLength(1000, ErrorMessage = "MSG.INVALID_INPUT")]
    public List<UserAuthRowRequest> Rows { get; set; } = [];
}

/// <summary>예외 권한 하나.</summary>
public sealed class UserAuthRowRequest
{
    /// <summary>프로그램 ID.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")]
    public string ProgramId { get; set; } = "";

    /// <summary>기능 코드.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(20, ErrorMessage = "MSG.INVALID_INPUT")]
    public string FuncCd { get; set; } = "";

    /// <summary>GRANT(추가) 또는 DENY(차단).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), RegularExpression("^(GRANT|DENY)$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string GrantType { get; set; } = "";

    /// <summary>종료일 (이날까지 적용).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT")]
    public DateOnly? EndDt { get; set; }
}

/// <summary>화면 모델.</summary>
/// <param name="Funcs">기능 이름 (공통코드 FUNC_CD).</param>
/// <param name="Companies">회사 검색 선택지.</param>
public sealed record UserAuthIndexModel(IReadOnlyList<(string Code, string Name)> Funcs, IReadOnlyList<(string Code, string Name)> Companies);
