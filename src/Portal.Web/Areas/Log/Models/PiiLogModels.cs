using System.ComponentModel.DataAnnotations;
using Portal.Core.Keys;

namespace Portal.Web.Areas.Log.Models;

/// <summary>개인정보 접속기록 검색 조건. 기간은 브라우저가 사용자 시간대의 날짜 경계를 UTC로 바꿔 보낸다.</summary>
public sealed class PiiLogSearch
{
    /// <summary>시작 (포함).</summary>
    [Required(ErrorMessage = LogMessageKeys.Period)]
    public DateTimeOffset? FromDt { get; set; }

    /// <summary>끝 (제외). 시작부터 31일까지.</summary>
    [Required(ErrorMessage = LogMessageKeys.Period)]
    public DateTimeOffset? ToDt { get; set; }

    /// <summary>처리한 사용자 ID (정확히 일치).</summary>
    [StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? UserId { get; set; }

    /// <summary>프로그램.</summary>
    [StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? ProgramId { get; set; }

    /// <summary>작업 VIEW(조회)/DOWNLOAD(다운로드)/UNMASK(마스킹 해제).</summary>
    [RegularExpression("^(VIEW|DOWNLOAD|UNMASK)$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string? ActionCd { get; set; }

    /// <summary>접속 IP (정확히 일치).</summary>
    [StringLength(45, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? Ip { get; set; }

    /// <summary>쪽 번호.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "MSG.INVALID_INPUT")]
    public int PageNo { get; set; } = 1;
}

/// <summary>개인정보 접속기록 엑셀 내보내기 요청. 사용자 ID·접속 IP가 들어 있어 사유를 묻고, 이 내려받기도 개인정보 접속기록에 남긴다.</summary>
public sealed class PiiLogExportRequest
{
    /// <summary>목록과 같은 검색 조건 (쪽 번호는 쓰지 않는다).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT")]
    public PiiLogSearch Search { get; set; } = new();

    /// <summary>다운로드 사유.</summary>
    [Required(ErrorMessage = MessageKeys.PiiReasonRequired), StringLength(200, MinimumLength = 2, ErrorMessage = MessageKeys.PiiReasonRequired)]
    public string Reason { get; set; } = "";
}

/// <summary>개인정보 접속기록 (<c>LOG.USP_PII_ACCESS_LOG_GET_LIST</c>).</summary>
public sealed class PiiLogDto
{
    /// <summary>로그 ID.</summary>
    public long LogId { get; set; }

    /// <summary>일시 (UTC).</summary>
    public DateTime LogDt { get; set; }

    /// <summary>처리한 사용자 ID.</summary>
    public string UserId { get; set; } = "";

    /// <summary>프로그램 ID.</summary>
    public string ProgramId { get; set; } = "";

    /// <summary>화면 이름 (메뉴명, 서비스가 채운다).</summary>
    public string? ProgramNm { get; set; }

    /// <summary>작업 VIEW/DOWNLOAD/UNMASK.</summary>
    public string ActionCd { get; set; } = "";

    /// <summary>대상 (건수·조건 JSON, 개인정보 원문 없음).</summary>
    public string? TargetDesc { get; set; }

    /// <summary>사유 (다운로드·마스킹 해제).</summary>
    public string? Reason { get; set; }

    /// <summary>접속 IP.</summary>
    public string? ClientIp { get; set; }
}

/// <summary>화면 모델.</summary>
/// <param name="Programs">메뉴에 있는 프로그램 (ID, 화면 이름).</param>
public sealed record PiiLogIndexModel(IReadOnlyList<(string Code, string Name)> Programs);
