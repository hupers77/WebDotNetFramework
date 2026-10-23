using System.ComponentModel.DataAnnotations;
using Portal.Framework.Web.Mvc;

namespace Portal.Web.Areas.Sys.Models;

/// <summary>API 클라이언트 한 건 (<c>FW.USP_API_CLIENT_ADMIN_GET_LIST</c>). 비밀값 해시는 내보내지 않는다.</summary>
public sealed class ApiClientDto
{
    /// <summary>클라이언트 ID.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>이름.</summary>
    public string ClientNm { get; set; } = "";

    /// <summary>허용한 API 코드(쉼표 구분).</summary>
    public string ApiCodes { get; set; } = "";

    /// <summary>허용 IP(쉼표 구분 CIDR/IP, 비면 제한 없음).</summary>
    public string? AllowIpList { get; set; }

    /// <summary>분당 호출 한도 (비면 기본값).</summary>
    public int? RatePerMin { get; set; }

    /// <summary>사용 기한 (yyyy-MM-dd, 포함).</summary>
    public string? ExpireDt { get; set; }

    /// <summary>사용 여부.</summary>
    public string UseYn { get; set; } = "Y";

    /// <summary>비밀값 발급 여부(Y/N).</summary>
    public string SecretYn { get; set; } = "N";

    /// <summary>비밀값을 마지막으로 발급한 일시 (UTC).</summary>
    public DateTime? SecretIssuedDt { get; set; }

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

/// <summary>화면에 보여 줄 부가 정보.</summary>
public sealed class ApiClientIndexModel
{
    /// <summary>현재 등록된 API 코드와 설명 (허용 코드를 고를 때 참고).</summary>
    public IReadOnlyList<ApiCodeInfo> Codes { get; set; } = [];
}

/// <summary>등록된 API 한 개.</summary>
/// <param name="Code">API 코드.</param>
/// <param name="Method">HTTP 메서드.</param>
/// <param name="Path">경로.</param>
/// <param name="Summary">설명.</param>
public sealed record ApiCodeInfo(string Code, string Method, string Path, string Summary);

/// <summary>클라이언트 일괄 저장 요청.</summary>
public sealed class ApiClientSaveRequest
{
    /// <summary>바뀐 행.</summary>
    [MaxLength(200, ErrorMessage = "MSG.INVALID_INPUT")]
    public List<ApiClientSaveRow> Rows { get; set; } = [];
}

/// <summary>바뀐 클라이언트 한 건.</summary>
public sealed class ApiClientSaveRow : IGridRow
{
    /// <summary>행 상태 (I/U/D).</summary>
    public string RowStat { get; set; } = "";

    /// <summary>클라이언트 ID.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(50, ErrorMessage = "MSG.INVALID_INPUT")]
    public string ClientId { get; set; } = "";

    /// <summary>이름 (삭제 행은 비어도 된다).</summary>
    [StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? ClientNm { get; set; }

    /// <summary>허용 API 코드(쉼표 구분).</summary>
    [StringLength(2000, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? ApiCodes { get; set; }

    /// <summary>허용 IP.</summary>
    [StringLength(2000, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? AllowIpList { get; set; }

    /// <summary>분당 호출 한도.</summary>
    [Range(1, 100000, ErrorMessage = "MSG.INVALID_INPUT")]
    public int? RatePerMin { get; set; }

    /// <summary>사용 기한 (yyyy-MM-dd).</summary>
    [StringLength(10, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? ExpireDt { get; set; }

    /// <summary>사용 여부.</summary>
    public string UseYn { get; set; } = "Y";

    /// <summary>조회 때 받은 값 (수정·삭제 필수).</summary>
    public byte[]? RowVer { get; set; }
}

/// <summary>저장 결과.</summary>
/// <param name="AffectedCnt">처리된 행 수.</param>
public sealed record ApiClientSaveResult(int AffectedCnt);

/// <summary>비밀값 발급 요청.</summary>
public sealed class ApiClientSecretRequest
{
    /// <summary>클라이언트 ID.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(50, ErrorMessage = "MSG.INVALID_INPUT")]
    public string ClientId { get; set; } = "";

    /// <summary>조회 때 받은 값.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT")]
    public byte[]? RowVer { get; set; }
}

/// <summary>발급한 비밀값. 이 응답에서 한 번만 보여 주고 어디에도 저장·기록하지 않는다.</summary>
/// <param name="ClientId">클라이언트 ID.</param>
/// <param name="ClientSecret">비밀값 원문.</param>
public sealed record ApiClientSecretResult(string ClientId, string ClientSecret);
