using System.ComponentModel.DataAnnotations;
using Portal.Core.Keys;

namespace Portal.Web.Areas.Log.Models;

/// <summary>데이터 변경 기록 검색 조건. 기간은 브라우저가 사용자 시간대의 날짜 경계를 UTC로 바꿔 보낸다.</summary>
public sealed class AuditLogSearch
{
    /// <summary>시작 (포함).</summary>
    [Required(ErrorMessage = LogMessageKeys.Period)]
    public DateTimeOffset? FromDt { get; set; }

    /// <summary>끝 (제외). 시작부터 31일까지.</summary>
    [Required(ErrorMessage = LogMessageKeys.Period)]
    public DateTimeOffset? ToDt { get; set; }

    /// <summary>테이블 (예: <c>FW.TB_USER</c>, 정확히 일치).</summary>
    [StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? TableNm { get; set; }

    /// <summary>작업 I(등록)/U(수정)/D(삭제).</summary>
    [RegularExpression("^[IUD]$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string? ActionCd { get; set; }

    /// <summary>대상 키 앞부분 (예: 사용자 ID, 코드 그룹).</summary>
    [StringLength(200, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? RowKey { get; set; }

    /// <summary>변경한 사용자 ID (정확히 일치).</summary>
    [StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? UserId { get; set; }

    /// <summary>접속 IP (정확히 일치).</summary>
    [StringLength(45, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? Ip { get; set; }

    /// <summary>쪽 번호.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "MSG.INVALID_INPUT")]
    public int PageNo { get; set; } = 1;
}

/// <summary>데이터 변경 기록 엑셀 내보내기 요청. 변경 내용에 이름 등 개인정보가 있을 수 있어 사유를 묻고 개인정보 접속기록에 남긴다.</summary>
public sealed class AuditLogExportRequest
{
    /// <summary>목록과 같은 검색 조건 (쪽 번호는 쓰지 않는다).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT")]
    public AuditLogSearch Search { get; set; } = new();

    /// <summary>다운로드 사유.</summary>
    [Required(ErrorMessage = MessageKeys.PiiReasonRequired), StringLength(200, MinimumLength = 2, ErrorMessage = MessageKeys.PiiReasonRequired)]
    public string Reason { get; set; } = "";
}

/// <summary>데이터 변경 기록 (<c>LOG.USP_AUDIT_LOG_GET_LIST</c>, <c>LOG.USP_AUDIT_LOG_GET</c>).</summary>
public sealed class AuditLogDto
{
    /// <summary>로그 ID.</summary>
    public long LogId { get; set; }

    /// <summary>일시 (UTC). 단건 조회에 다시 넘긴다(파티션 키).</summary>
    public DateTime LogDt { get; set; }

    /// <summary>변경한 사용자 ID.</summary>
    public string? UserId { get; set; }

    /// <summary>테이블.</summary>
    public string TableNm { get; set; } = "";

    /// <summary>작업 I/U/D.</summary>
    public string ActionCd { get; set; } = "";

    /// <summary>대상 키.</summary>
    public string RowKey { get; set; } = "";

    /// <summary>접속 IP.</summary>
    public string? ClientIp { get; set; }

    /// <summary>변경 전 (단건 조회·엑셀만).</summary>
    public string? BeforeJson { get; set; }

    /// <summary>변경 후 (단건 조회·엑셀만).</summary>
    public string? AfterJson { get; set; }
}

/// <summary>화면 모델.</summary>
/// <param name="Tables">기록이 있는 테이블 이름.</param>
public sealed record AuditLogIndexModel(IReadOnlyList<string> Tables);
