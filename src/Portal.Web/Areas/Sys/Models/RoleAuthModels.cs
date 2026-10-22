using System.ComponentModel.DataAnnotations;

namespace Portal.Web.Areas.Sys.Models;

/// <summary>역할별 권한 SP가 던지는 메시지 키.</summary>
public static class RoleAuthMessageKeys
{
    /// <summary>조회 없이 다른 기능만 줄 수 없다.</summary>
    public const string ReadRequired = "MSG.ROLE_AUTH_READ_REQUIRED";

    /// <summary>시스템 관리자 역할은 모든 권한을 가지므로 고치지 않는다.</summary>
    public const string SysAdmin = "MSG.ROLE_AUTH_SYS_ADMIN";

    /// <summary>자기가 가진 역할의 권한은 다른 관리자가 바꿔야 한다.</summary>
    public const string Self = "MSG.ROLE_AUTH_SELF";

    /// <summary>역할의 회사 유형(본사/협력사)에 열리지 않은 화면이다.</summary>
    public const string Scope = "MSG.ROLE_AUTH_SCOPE";
}

/// <summary>역할별 권한 화면 모델: 표의 기능 열 (코드, 이름, 공통코드 FUNC_CD 순서).</summary>
public sealed record RoleAuthIndexModel(IReadOnlyList<(string Code, string Name)> Funcs);

/// <summary>권한표 원천 행 (<c>FW.USP_ROLE_AUTH_GET_LIST</c>, 프로그램 × 기능).</summary>
public sealed class RoleAuthRow
{
    /// <summary>프로그램 ID.</summary>
    public string ProgramId { get; set; } = "";

    /// <summary>프로그램 관리용 이름 (메뉴에 없을 때 쓴다).</summary>
    public string ProgramNm { get; set; } = "";

    /// <summary>모듈.</summary>
    public string ModuleCd { get; set; } = "";

    /// <summary>공개 대상 ALL/EMP/PARTNER.</summary>
    public string OpenScopeCd { get; set; } = "ALL";

    /// <summary>역할의 회사 유형에 줄 수 있는지.</summary>
    public string GrantableYn { get; set; } = "Y";

    /// <summary>기능 코드.</summary>
    public string FuncCd { get; set; } = "";

    /// <summary>부여 여부.</summary>
    public string GrantedYn { get; set; } = "N";
}

/// <summary>권한표 한 줄 (프로그램 하나). 메뉴 순서로 정렬하고 최상위 메뉴로 묶는다.</summary>
/// <param name="GroupNm">최상위 메뉴명 (메뉴에 없으면 '메뉴에 없는 화면').</param>
/// <param name="ProgramId">프로그램 ID.</param>
/// <param name="ProgramNm">화면 이름 (메뉴명).</param>
/// <param name="OpenScopeCd">공개 대상 ALL/EMP/PARTNER.</param>
/// <param name="Grantable">역할의 회사 유형에 새로 줄 수 있는지 (이미 준 권한은 뺄 수 있다).</param>
/// <param name="Funcs">이 프로그램이 쓰는 기능과 부여 여부.</param>
public sealed record RoleAuthProgram(string GroupNm, string ProgramId, string ProgramNm, string OpenScopeCd, bool Grantable, IReadOnlyList<RoleAuthFuncState> Funcs);

/// <summary>프로그램 하나의 기능 하나.</summary>
/// <param name="FuncCd">기능 코드.</param>
/// <param name="Granted">부여 여부.</param>
public sealed record RoleAuthFuncState(string FuncCd, bool Granted);

/// <summary>역할 한 개의 권한 저장 요청 (체크된 기능 전부).</summary>
public sealed class RoleAuthSaveRequest
{
    /// <summary>역할 ID.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")]
    public string RoleId { get; set; } = "";

    /// <summary>목록에서 받은 역할 ROW_VER.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT")]
    public byte[]? RowVer { get; set; }

    /// <summary>부여할 기능 (사용 중인 프로그램 전부 기준).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT")]
    public List<RoleAuthFunc> Funcs { get; set; } = [];
}

/// <summary>부여할 기능 하나.</summary>
public sealed class RoleAuthFunc
{
    /// <summary>프로그램 ID.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")]
    public string ProgramId { get; set; } = "";

    /// <summary>기능 코드.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(20, ErrorMessage = "MSG.INVALID_INPUT")]
    public string FuncCd { get; set; } = "";
}
