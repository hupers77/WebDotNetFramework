using System.ComponentModel.DataAnnotations;

namespace Portal.Web.Areas.Sys.Models;

/// <summary>역할 관리 SP가 던지는 메시지 키.</summary>
public static class RoleMessageKeys
{
    /// <summary>협력사 역할은 데이터 범위를 전체로 둘 수 없다.</summary>
    public const string PartnerScope = "MSG.ROLE_PARTNER_SCOPE";

    /// <summary>시스템 관리자 역할은 시스템 관리자만 고치고, 범위·회사 유형·사용 여부는 바꿀 수 없다.</summary>
    public const string SysAdmin = "MSG.ROLE_SYS_ADMIN";

    /// <summary>시스템 역할은 지우거나 사용 안 함·회사 유형 변경을 할 수 없다.</summary>
    public const string System = "MSG.ROLE_SYSTEM";

    /// <summary>자기가 가진 역할은 다른 관리자가 고쳐야 한다.</summary>
    public const string Self = "MSG.ROLE_SELF";

    /// <summary>부여됐거나 기능 권한이 있는 역할은 회사 유형을 바꿀 수 없다.</summary>
    public const string TypeFixed = "MSG.ROLE_TYPE_FIXED";

    /// <summary>계정·부서·AD 그룹·위임에 부여된 역할은 지울 수 없다.</summary>
    public const string InUse = "MSG.ROLE_IN_USE";
}

/// <summary>역할 (<c>FW.USP_ROLE_GET_LIST</c>). 역할 관리와 역할별 권한 화면이 같이 쓴다.</summary>
public sealed class RoleDto
{
    /// <summary>역할 ID.</summary>
    public string RoleId { get; set; } = "";

    /// <summary>역할명.</summary>
    public string RoleNm { get; set; } = "";

    /// <summary>설명.</summary>
    public string? Description { get; set; }

    /// <summary>줄 수 있는 회사 유형 (HQ/PARTNER, 없으면 모두).</summary>
    public string? CompanyTypeCd { get; set; }

    /// <summary>데이터 범위 (공통코드 <c>DATA_SCOPE</c>).</summary>
    public string DataScopeCd { get; set; } = "";

    /// <summary>시스템 역할 (삭제·사용 안 함·회사 유형 변경 불가).</summary>
    public string SystemYn { get; set; } = "N";

    /// <summary>정렬 순서.</summary>
    public int SortNo { get; set; }

    /// <summary>사용 여부.</summary>
    public string UseYn { get; set; } = "Y";

    /// <summary>부여된 기능 수 (사용 중인 프로그램).</summary>
    public int FuncCnt { get; set; }

    /// <summary>직접 부여한 계정 수.</summary>
    public int UserCnt { get; set; }

    /// <summary>부여한 부서 수.</summary>
    public int DeptCnt { get; set; }

    /// <summary>계정·부서·AD 그룹·위임 중 하나라도 부여됐는지 (Y면 지울 수 없다).</summary>
    public string InUseYn { get; set; } = "N";

    /// <summary>현재 사용자가 가진 역할인지 (직접·부서·AD 그룹·위임).</summary>
    public string MyRoleYn { get; set; } = "N";

    /// <summary>마지막 수정자.</summary>
    public string UpdId { get; set; } = "";

    /// <summary>마지막 수정 (UTC).</summary>
    public DateTime UpdDt { get; set; }

    /// <summary>동시성 확인용.</summary>
    public byte[] RowVer { get; set; } = [];
}

/// <summary>역할 등록·수정 요청. <see cref="RowVer"/>가 없으면 등록.</summary>
public sealed class RoleSaveRequest
{
    /// <summary>역할 ID (영문 대문자로 시작, 대문자·숫자·밑줄 2~30자). 등록한 뒤에는 바꿀 수 없다.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), RegularExpression("^[A-Z][A-Z0-9_]{1,29}$", ErrorMessage = "VAL.ROLE_ID_FORMAT")]
    public string RoleId { get; set; } = "";

    /// <summary>수정할 때 조회에서 받은 값. 등록이면 비운다.</summary>
    [Length(8, 8, ErrorMessage = "MSG.INVALID_INPUT")]
    public byte[]? RowVer { get; set; }

    /// <summary>역할명.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string RoleNm { get; set; } = "";

    /// <summary>설명.</summary>
    [StringLength(500, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? Description { get; set; }

    /// <summary>줄 수 있는 회사 유형 (HQ/PARTNER). 비우면 모두.</summary>
    [RegularExpression("^(HQ|PARTNER)$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string? CompanyTypeCd { get; set; }

    /// <summary>데이터 범위 (ALL/COMPANY/DEPT_SUB/DEPT/SELF).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), RegularExpression("^(ALL|COMPANY|DEPT_SUB|DEPT|SELF)$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string DataScopeCd { get; set; } = "SELF";

    /// <summary>정렬 순서.</summary>
    [Range(0, 99999, ErrorMessage = "MSG.INVALID_INPUT")]
    public int SortNo { get; set; }

    /// <summary>사용 여부 (Y/N).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), RegularExpression("^[YN]$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string UseYn { get; set; } = "Y";
}

/// <summary>역할 삭제 요청.</summary>
public sealed class RoleDeleteRequest
{
    /// <summary>역할 ID.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")]
    public string RoleId { get; set; } = "";

    /// <summary>조회 때 받은 값.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), Length(8, 8, ErrorMessage = "MSG.INVALID_INPUT")]
    public byte[] RowVer { get; set; } = [];
}

/// <summary>화면 모델: 회사 유형·데이터 범위 선택지 (코드, 이름).</summary>
/// <param name="CompanyTypes">회사 유형.</param>
/// <param name="DataScopes">데이터 범위.</param>
public sealed record RoleIndexModel(IReadOnlyList<(string Code, string Name)> CompanyTypes, IReadOnlyList<(string Code, string Name)> DataScopes);
