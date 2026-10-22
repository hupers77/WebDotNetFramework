using System.ComponentModel.DataAnnotations;

namespace Portal.Web.Areas.Sys.Models;

/// <summary>부서 관리 SP가 던지는 메시지 키.</summary>
public static class DeptMessageKeys
{
    /// <summary>AD 동기화 부서는 이름·상위·사용 여부를 바꾸거나 지울 수 없다.</summary>
    public const string AdManaged = "MSG.DEPT_AD_MANAGED";

    /// <summary>사용 안 함 부서 아래에 사용 중인 부서를 둘 수 없다.</summary>
    public const string ParentUnused = "MSG.DEPT_PARENT_UNUSED";

    /// <summary>자기 자신이나 하위 부서를 상위로 고를 수 없다.</summary>
    public const string ParentCycle = "MSG.DEPT_PARENT_CYCLE";

    /// <summary>사용 중인 하위 부서가 있어 사용 안 함으로 바꿀 수 없다.</summary>
    public const string ChildInUse = "MSG.DEPT_CHILD_IN_USE";

    /// <summary>부서에 줄 수 없는 역할(회사 유형이 다르거나 SYS_ADMIN).</summary>
    public const string RoleType = "MSG.DEPT_ROLE_TYPE";

    /// <summary>하위 부서나 소속 계정이 있어 지울 수 없다.</summary>
    public const string InUse = "MSG.DEPT_IN_USE";
}

/// <summary>부서 트리 조건.</summary>
public sealed class DeptSearch
{
    /// <summary>회사 코드.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(10, ErrorMessage = "MSG.INVALID_INPUT")]
    public string CompanyCd { get; set; } = "";

    /// <summary>Y면 사용 안 함 부서도.</summary>
    [RegularExpression("^[YN]$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string IncludeUnusedYn { get; set; } = "N";
}

/// <summary>부서 (<c>FW.USP_DEPT_GET_LIST</c>). 화면이 <see cref="ParentDeptId"/>로 트리를 만든다.</summary>
public sealed class DeptDto
{
    /// <summary>부서 ID.</summary>
    public string DeptId { get; set; } = "";

    /// <summary>회사 코드.</summary>
    public string CompanyCd { get; set; } = "";

    /// <summary>상위 부서 ID. 최상위면 null.</summary>
    public string? ParentDeptId { get; set; }

    /// <summary>부서명.</summary>
    public string DeptNm { get; set; } = "";

    /// <summary>AD 동기화 부서면 Y (이름·상위·사용 여부는 동기화가 정한다).</summary>
    public string AdYn { get; set; } = "N";

    /// <summary>정렬 순서.</summary>
    public int SortNo { get; set; }

    /// <summary>사용 여부.</summary>
    public string UseYn { get; set; } = "Y";

    /// <summary>이 부서 소속 계정 수 (하위 부서 제외).</summary>
    public int UserCnt { get; set; }

    /// <summary>바로 아래 부서 수.</summary>
    public int ChildCnt { get; set; }

    /// <summary>부서 역할 (쉼표 구분).</summary>
    public string? RoleIds { get; set; }

    /// <summary>마지막 수정자.</summary>
    public string UpdId { get; set; } = "";

    /// <summary>마지막 수정 (UTC).</summary>
    public DateTime UpdDt { get; set; }

    /// <summary>동시성 확인용.</summary>
    public byte[] RowVer { get; set; } = [];
}

/// <summary>부서 역할 선택지 (<c>FW.USP_DEPT_ROLE_GET_LIST</c>).</summary>
public sealed class DeptRoleDto
{
    /// <summary>역할 ID.</summary>
    public string RoleId { get; set; } = "";

    /// <summary>역할명.</summary>
    public string RoleNm { get; set; } = "";

    /// <summary>설명.</summary>
    public string? Description { get; set; }

    /// <summary>줄 수 있는 회사 유형. null이면 모두.</summary>
    public string? CompanyTypeCd { get; set; }

    /// <summary>이 부서에 준 역할이면 Y.</summary>
    public string AssignedYn { get; set; } = "N";

    /// <summary>하위 부서에도 적용하면 Y (주지 않은 역할은 기본값 Y).</summary>
    public string InheritYn { get; set; } = "Y";

    /// <summary>이 부서에 줄 수 있으면 Y.</summary>
    public string AssignableYn { get; set; } = "N";
}

/// <summary>부서에 주는 역할 하나.</summary>
public sealed class DeptRoleItem
{
    /// <summary>역할 ID.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, MinimumLength = 1, ErrorMessage = "MSG.INVALID_INPUT")]
    public string RoleId { get; set; } = "";

    /// <summary>하위 부서에도 적용 (Y/N).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), RegularExpression("^[YN]$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string InheritYn { get; set; } = "Y";
}

/// <summary>부서 등록·수정 요청. <see cref="RowVer"/>가 없으면 등록.</summary>
public sealed class DeptSaveRequest
{
    /// <summary>부서 ID. 수정할 때만 넣는다(등록하면 SP가 D000001부터 차례로 부여한다).</summary>
    [StringLength(20, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? DeptId { get; set; }

    /// <summary>수정할 때 조회에서 받은 값. 등록이면 비운다.</summary>
    [Length(8, 8, ErrorMessage = "MSG.INVALID_INPUT")]
    public byte[]? RowVer { get; set; }

    /// <summary>회사 코드. 수정할 때 바꿀 수 없다.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(10, ErrorMessage = "MSG.INVALID_INPUT")]
    public string CompanyCd { get; set; } = "";

    /// <summary>상위 부서 ID. 비우면 최상위.</summary>
    [StringLength(20, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? ParentDeptId { get; set; }

    /// <summary>부서명.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string DeptNm { get; set; } = "";

    /// <summary>정렬 순서.</summary>
    [Range(0, 99999, ErrorMessage = "MSG.INVALID_INPUT")]
    public int SortNo { get; set; }

    /// <summary>사용 여부 (Y/N).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), RegularExpression("^[YN]$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string UseYn { get; set; } = "Y";

    /// <summary>부서 역할 (이 목록이 되도록 더하고 뺀다).</summary>
    [MaxLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public List<DeptRoleItem> Roles { get; set; } = [];
}

/// <summary>부서 삭제 요청.</summary>
public sealed class DeptDeleteRequest
{
    /// <summary>부서 ID.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(20, ErrorMessage = "MSG.INVALID_INPUT")]
    public string DeptId { get; set; } = "";

    /// <summary>조회 때 받은 값.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), Length(8, 8, ErrorMessage = "MSG.INVALID_INPUT")]
    public byte[] RowVer { get; set; } = [];
}

/// <summary>화면 모델: 회사 선택지 (본사 먼저).</summary>
/// <param name="Companies">(회사 코드, 회사명).</param>
public sealed record DeptIndexModel(IReadOnlyList<(string Code, string Name)> Companies);
