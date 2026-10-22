using System.ComponentModel.DataAnnotations;

namespace Portal.Web.Areas.Sys.Models;

/// <summary>회사 관리 SP·서비스가 던지는 메시지 키.</summary>
public static class CompanyMessageKeys
{
    /// <summary>본사는 사용 안 함·계약 종료일·삭제·새로 등록할 수 없다.</summary>
    public const string HqRule = "MSG.COMPANY_HQ_RULE";

    /// <summary>회사 유형은 바꿀 수 없다.</summary>
    public const string TypeFixed = "MSG.COMPANY_TYPE_FIXED";

    /// <summary>협력사는 사업자등록번호가 있어야 한다.</summary>
    public const string BizRequired = "MSG.COMPANY_BIZ_REQUIRED";

    /// <summary>다른 협력사와 사업자등록번호가 같다.</summary>
    public const string BizDuplicate = "MSG.COMPANY_BIZ_DUP";

    /// <summary>허용 IP 형식 오류.</summary>
    public const string IpInvalid = "MSG.COMPANY_IP_INVALID";

    /// <summary>지금 접속한 IP가 빠지면 저장하는 사람도 로그인할 수 없게 된다.</summary>
    public const string IpSelfLockout = "MSG.COMPANY_IP_SELF";

    /// <summary>계정·부서·가입 신청·첨부 파일이 있어 지울 수 없다.</summary>
    public const string InUse = "MSG.COMPANY_IN_USE";
}

/// <summary>회사 유형 (공통코드 <c>COMPANY_TYPE</c>).</summary>
public static class CompanyTypes
{
    /// <summary>본사.</summary>
    public const string Hq = "HQ";

    /// <summary>협력사.</summary>
    public const string Partner = "PARTNER";
}

/// <summary>회사 목록 조건.</summary>
public sealed class CompanySearch
{
    /// <summary>회사 코드·회사명·사업자등록번호 일부.</summary>
    [StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? Keyword { get; set; }

    /// <summary>회사 유형 (HQ/PARTNER). 비우면 전체.</summary>
    [RegularExpression("^(HQ|PARTNER)$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string? CompanyTypeCd { get; set; }

    /// <summary>사용 여부 (Y/N). 비우면 전체.</summary>
    [RegularExpression("^[YN]$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string? UseYn { get; set; }

    /// <summary>쪽 번호.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "MSG.INVALID_INPUT")]
    public int PageNo { get; set; } = 1;
}

/// <summary>회사 (<c>FW.USP_COMPANY_GET_LIST</c>).</summary>
public sealed class CompanyDto
{
    /// <summary>회사 코드.</summary>
    public string CompanyCd { get; set; } = "";

    /// <summary>회사명.</summary>
    public string CompanyNm { get; set; } = "";

    /// <summary>HQ / PARTNER.</summary>
    public string CompanyTypeCd { get; set; } = "";

    /// <summary>회사 유형명 (사용자 언어).</summary>
    public string CompanyTypeNm { get; set; } = "";

    /// <summary>사업자등록번호 (법인 정보).</summary>
    public string? BizRegNo { get; set; }

    /// <summary>접속 허용 IP (쉼표 구분 CIDR). 비었으면 제한 없음.</summary>
    public string? AllowIpList { get; set; }

    /// <summary>계약 종료일.</summary>
    public DateOnly? ContractEndDt { get; set; }

    /// <summary>정렬 순서.</summary>
    public int SortNo { get; set; }

    /// <summary>사용 여부.</summary>
    public string UseYn { get; set; } = "Y";

    /// <summary>소속 계정 수.</summary>
    public int UserCnt { get; set; }

    /// <summary>마지막 수정자.</summary>
    public string UpdId { get; set; } = "";

    /// <summary>마지막 수정 (UTC).</summary>
    public DateTime UpdDt { get; set; }

    /// <summary>동시성 확인용. 저장·삭제할 때 그대로 돌려보낸다.</summary>
    public byte[] RowVer { get; set; } = [];
}

/// <summary>회사 등록·수정 요청. <see cref="RowVer"/>가 없으면 등록(협력사만).</summary>
public sealed class CompanySaveRequest
{
    /// <summary>회사 코드. 수정할 때만 넣는다(등록하면 SP가 P0001부터 차례로 부여하고, 바꿀 수 없다).</summary>
    [StringLength(10, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? CompanyCd { get; set; }

    /// <summary>수정할 때 조회에서 받은 값. 등록이면 비운다.</summary>
    [Length(8, 8, ErrorMessage = "MSG.INVALID_INPUT")]
    public byte[]? RowVer { get; set; }

    /// <summary>회사명.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string CompanyNm { get; set; } = "";

    /// <summary>HQ / PARTNER. 수정할 때 바꿀 수 없다.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), RegularExpression("^(HQ|PARTNER)$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string CompanyTypeCd { get; set; } = CompanyTypes.Partner;

    /// <summary>사업자등록번호. 협력사는 필수.</summary>
    [StringLength(20, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? BizRegNo { get; set; }

    /// <summary>접속 허용 IP (쉼표·줄바꿈 구분 CIDR 또는 단일 IP). 비우면 제한 없음.</summary>
    [StringLength(1000, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? AllowIpList { get; set; }

    /// <summary>계약 종료일 (협력사). 비우면 기한 없음.</summary>
    public DateOnly? ContractEndDt { get; set; }

    /// <summary>정렬 순서.</summary>
    [Range(0, 99999, ErrorMessage = "MSG.INVALID_INPUT")]
    public int SortNo { get; set; }

    /// <summary>사용 여부 (Y/N).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), RegularExpression("^[YN]$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string UseYn { get; set; } = "Y";
}

/// <summary>회사 삭제 요청.</summary>
public sealed class CompanyDeleteRequest
{
    /// <summary>회사 코드.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(10, ErrorMessage = "MSG.INVALID_INPUT")]
    public string CompanyCd { get; set; } = "";

    /// <summary>조회 때 받은 값.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), Length(8, 8, ErrorMessage = "MSG.INVALID_INPUT")]
    public byte[] RowVer { get; set; } = [];
}

/// <summary>저장 결과: 회사 코드와 사용 기한을 함께 바꾼 협력사 계정 수.</summary>
/// <param name="CompanyCd">회사 코드.</param>
/// <param name="MovedUserCnt">계약 종료일을 따라 사용 기한을 바꾼 계정 수.</param>
public sealed record CompanySaveResult(string CompanyCd, int MovedUserCnt);

/// <summary>화면 모델: 회사 유형 선택지.</summary>
/// <param name="Types">(코드, 이름).</param>
public sealed record CompanyIndexModel(IReadOnlyList<(string Code, string Name)> Types);
