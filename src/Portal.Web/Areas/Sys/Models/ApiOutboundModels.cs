using System.ComponentModel.DataAnnotations;
using Portal.Framework.Web.Mvc;

namespace Portal.Web.Areas.Sys.Models;

/// <summary>송신 대상 한 건 (<c>FW.USP_API_OUTBOUND_ADMIN_GET_LIST</c>). 인증 비밀값은 내보내지 않고 설정 여부만 보낸다.</summary>
public sealed class ApiOutboundDto
{
    /// <summary>대상 키.</summary>
    public string TargetKey { get; set; } = "";

    /// <summary>이름.</summary>
    public string TargetNm { get; set; } = "";

    /// <summary>기본 주소.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>인증 방식: NONE/BASIC/BEARER/API_KEY.</summary>
    public string AuthTypeCd { get; set; } = "NONE";

    /// <summary>API_KEY일 때 헤더 이름.</summary>
    public string? AuthHeader { get; set; }

    /// <summary>BASIC일 때 사용자.</summary>
    public string? AuthUser { get; set; }

    /// <summary>인증 비밀값이 저장돼 있는지(Y/N).</summary>
    public string SecretYn { get; set; } = "N";

    /// <summary>제한 시간(초).</summary>
    public int TimeoutSec { get; set; }

    /// <summary>재시도 횟수.</summary>
    public int RetryCount { get; set; }

    /// <summary>사내망(사설 IP) 허용.</summary>
    public string AllowPrivateYn { get; set; } = "N";

    /// <summary>사용 여부.</summary>
    public string UseYn { get; set; } = "Y";

    /// <summary>마지막 수정자.</summary>
    public string UpdId { get; set; } = "";

    /// <summary>마지막 수정 일시 (UTC).</summary>
    public DateTime UpdDt
    {
        get;
        set => field = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    /// <summary>동시성 확인용.</summary>
    public byte[] RowVer { get; set; } = [];
}

/// <summary>송신 대상 일괄 저장 요청.</summary>
public sealed class ApiOutboundSaveRequest
{
    /// <summary>바뀐 행.</summary>
    [MaxLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public List<ApiOutboundSaveRow> Rows { get; set; } = [];
}

/// <summary>바뀐 송신 대상 한 건.</summary>
public sealed class ApiOutboundSaveRow : IGridRow
{
    /// <summary>행 상태 (I/U/D).</summary>
    public string RowStat { get; set; } = "";

    /// <summary>대상 키.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")]
    public string TargetKey { get; set; } = "";

    /// <summary>이름 (삭제 행은 비어도 된다).</summary>
    [StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? TargetNm { get; set; }

    /// <summary>기본 주소.</summary>
    [StringLength(500, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? BaseUrl { get; set; }

    /// <summary>인증 방식.</summary>
    [StringLength(10, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? AuthTypeCd { get; set; }

    /// <summary>API_KEY 헤더 이름.</summary>
    [StringLength(50, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? AuthHeader { get; set; }

    /// <summary>BASIC 사용자.</summary>
    [StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? AuthUser { get; set; }

    /// <summary>새 인증 비밀값. 비우면 기존 값을 유지한다.</summary>
    [StringLength(1000, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? NewSecret { get; set; }

    /// <summary>제한 시간(초).</summary>
    [Range(1, 300, ErrorMessage = "MSG.INVALID_INPUT")]
    public int TimeoutSec { get; set; } = 30;

    /// <summary>재시도 횟수.</summary>
    [Range(0, 5, ErrorMessage = "MSG.INVALID_INPUT")]
    public int RetryCount { get; set; } = 2;

    /// <summary>사내망 허용.</summary>
    public string AllowPrivateYn { get; set; } = "N";

    /// <summary>사용 여부.</summary>
    public string UseYn { get; set; } = "Y";

    /// <summary>조회 때 받은 값 (수정·삭제 필수).</summary>
    public byte[]? RowVer { get; set; }
}

/// <summary>저장 결과.</summary>
/// <param name="AffectedCnt">처리된 행 수.</param>
public sealed record ApiOutboundSaveResult(int AffectedCnt);
