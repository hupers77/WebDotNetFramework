using System.ComponentModel.DataAnnotations;

namespace Portal.Web.Areas.Sys.Models;

/// <summary>스케줄 관리 SP가 던지는 메시지 키.</summary>
public static class JobMessageKeys
{
    /// <summary>주기(Cron) 형식 오류.</summary>
    public const string CronInvalid = "MSG.JOB_CRON_INVALID";

    /// <summary>사용하지 않는 작업은 실행할 수 없다.</summary>
    public const string NotInUse = "MSG.JOB_NOT_IN_USE";
}

/// <summary>작업 목록 조건.</summary>
public sealed class JobSearch
{
    /// <summary>작업 ID·작업명 일부.</summary>
    [StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? Keyword { get; set; }
}

/// <summary>배치 작업 (<c>FW.USP_JOB_GET_LIST</c>).</summary>
public sealed class JobDto
{
    /// <summary>작업 ID.</summary>
    public string JobId { get; set; } = "";

    /// <summary>작업명.</summary>
    public string JobNm { get; set; } = "";

    /// <summary>CLASS / SP.</summary>
    public string JobTypeCd { get; set; } = "";

    /// <summary>실행 대상 (클래스 이름 또는 SP 이름).</summary>
    public string JobTarget { get; set; } = "";

    /// <summary>주기 (Cron). 없으면 수동 실행만.</summary>
    public string? CronExpr { get; set; }

    /// <summary>제한 시간(분).</summary>
    public int TimeoutMin { get; set; }

    /// <summary>설명.</summary>
    public string? Description { get; set; }

    /// <summary>사용 여부.</summary>
    public string UseYn { get; set; } = "Y";

    /// <summary>다음 예약 실행 (UTC). 비었으면 Worker가 계산 중.</summary>
    public DateTime? NextRunDt { get; set; }

    /// <summary>수동 실행 요청 여부.</summary>
    public string RunReqYn { get; set; } = "N";

    /// <summary>수동 실행 요청자.</summary>
    public string? RunReqId { get; set; }

    /// <summary>실행 중 여부.</summary>
    public string RunningYn { get; set; } = "N";

    /// <summary>마지막 실행 시작 (UTC).</summary>
    public DateTime? LastRunDt { get; set; }

    /// <summary>마지막 결과 (공통코드 JOB_STAT).</summary>
    public string? LastResultCd { get; set; }

    /// <summary>마지막 결과명 (사용자 언어).</summary>
    public string? LastResultNm { get; set; }

    /// <summary>동시성 확인용. 저장할 때 그대로 돌려보낸다.</summary>
    public int EditVer { get; set; }
}

/// <summary>실행 이력 (<c>FW.USP_JOB_HIST_GET_LIST</c>).</summary>
public sealed class JobHistDto
{
    /// <summary>이력 번호.</summary>
    public long HistId { get; set; }

    /// <summary>SCHEDULE / MANUAL.</summary>
    public string RunTypeCd { get; set; } = "";

    /// <summary>구분명.</summary>
    public string RunTypeNm { get; set; } = "";

    /// <summary>수동 실행 요청자.</summary>
    public string? RunUserId { get; set; }

    /// <summary>실행한 서버.</summary>
    public string? HostNm { get; set; }

    /// <summary>시작 (UTC).</summary>
    public DateTime StartDt { get; set; }

    /// <summary>끝 (UTC). 실행 중이면 비어 있다.</summary>
    public DateTime? EndDt { get; set; }

    /// <summary>결과 (공통코드 JOB_STAT).</summary>
    public string ResultCd { get; set; } = "";

    /// <summary>결과명.</summary>
    public string ResultNm { get; set; } = "";

    /// <summary>처리 건수.</summary>
    public int? ProcessedCnt { get; set; }

    /// <summary>메모·실패 사유.</summary>
    public string? ResultMsg { get; set; }
}

/// <summary>작업 설정 저장 요청. 실행 대상(유형·대상)은 바꿀 수 없다.</summary>
public sealed class JobSaveRequest
{
    /// <summary>작업 ID.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")]
    public string JobId { get; set; } = "";

    /// <summary>조회 때 받은 값.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "MSG.INVALID_INPUT")]
    public int EditVer { get; set; }

    /// <summary>주기 (Cron). 비우면 수동 실행만.</summary>
    [StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? CronExpr { get; set; }

    /// <summary>제한 시간(분), 1~1440.</summary>
    [Range(1, 1440, ErrorMessage = "MSG.INVALID_INPUT")]
    public int TimeoutMin { get; set; }

    /// <summary>사용 여부 (Y/N).</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), RegularExpression("^[YN]$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string UseYn { get; set; } = "Y";

    /// <summary>설명.</summary>
    [StringLength(500, ErrorMessage = "MSG.INVALID_INPUT")]
    public string? Description { get; set; }
}

/// <summary>수동 실행 요청.</summary>
public sealed class JobRunRequest
{
    /// <summary>작업 ID.</summary>
    [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")]
    public string JobId { get; set; } = "";
}
