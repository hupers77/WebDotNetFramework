using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Portal.Web.Areas.Sys.Models;

/// <summary>가입 신청 목록 조건.</summary>
public sealed class PartnerJoinSearch
{
    /// <summary>신청 상태 (공통코드 <c>JOIN_STAT</c>). 비우면 전체.</summary>
    [StringLength(10, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? ReqStatCd { get; set; }

    /// <summary>ID 앞부분, 이름·회사명 일부.</summary>
    [StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? Keyword { get; set; }
}

/// <summary>협력사 가입 신청 (<c>FW.USP_PARTNER_JOIN_GET_LIST</c>).</summary>
public sealed class PartnerJoinDto
{
    /// <summary>신청 번호.</summary>
    public long ReqId { get; set; }

    /// <summary>회사 코드.</summary>
    public string CompanyCd { get; set; } = "";

    /// <summary>회사명.</summary>
    public string CompanyNm { get; set; } = "";

    /// <summary>사업자등록번호.</summary>
    public string? BizRegNo { get; set; }

    /// <summary>희망 사용자 ID.</summary>
    public string UserId { get; set; } = "";

    /// <summary>이름.</summary>
    public string UserNm { get; set; } = "";

    /// <summary>암호화된 이메일. 화면에 보내지 않는다.</summary>
    [JsonIgnore]
    public byte[]? EmailEnc { get; set; }

    /// <summary>가린 이메일 (<c>ab***@example.com</c>).</summary>
    public string EmailMasked { get; set; } = "";

    /// <summary>신청 언어.</summary>
    public string LangCd { get; set; } = "";

    /// <summary>상태 (REQUESTED/APPROVED/REJECTED).</summary>
    public string ReqStatCd { get; set; } = "";

    /// <summary>상태명 (사용자 언어).</summary>
    public string ReqStatNm { get; set; } = "";

    /// <summary>신청 시각 (UTC).</summary>
    public DateTime ReqDt { get; set; }

    /// <summary>처리한 사용자.</summary>
    public string? ProcId { get; set; }

    /// <summary>처리 시각 (UTC).</summary>
    public DateTime? ProcDt { get; set; }

    /// <summary>반려 사유.</summary>
    public string? RejectReason { get; set; }

    /// <summary>동시성 확인용. 처리할 때 그대로 돌려보낸다.</summary>
    public byte[] RowVer { get; set; } = [];
}

/// <summary>승인·반려 요청.</summary>
public sealed class PartnerJoinProcessRequest
{
    /// <summary>신청 번호.</summary>
    [Range(1, long.MaxValue, ErrorMessage = "MSG.INVALID_INPUT")]
    public long ReqId { get; set; }

    /// <summary>조회 때 받은 값.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), Length(8, 8, ErrorMessage = "MSG.INVALID_INPUT")]
    public byte[] RowVer { get; set; } = [];

    /// <summary>반려 사유 (반려할 때 필수).</summary>
    [StringLength(500, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? RejectReason { get; set; }
}

/// <summary>화면 모델: 상태 선택 목록.</summary>
/// <param name="Stats">신청 상태 (코드, 이름).</param>
public sealed record PartnerApproveIndexModel(IReadOnlyList<(string Code, string Name)> Stats);
