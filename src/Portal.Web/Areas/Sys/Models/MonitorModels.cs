namespace Portal.Web.Areas.Sys.Models;

/// <summary>운영 현황 요약 (<c>FW.USP_MONITOR_GET</c> 결과셋 1).</summary>
public sealed class MonitorSummary
{
    /// <summary>DB 서버 시각 (UTC).</summary>
    public DateTime DbDt
    {
        get;
        set => field = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    /// <summary>접속 중인 세션 수.</summary>
    public int SessionCnt { get; set; }

    /// <summary>잠긴 계정 수.</summary>
    public int LockedCnt { get; set; }

    /// <summary>최근 24시간 로그인 실패 수.</summary>
    public int LoginFailCnt { get; set; }

    /// <summary>최근 24시간 오류 수.</summary>
    public int ErrorCnt { get; set; }
}

/// <summary>캐시 버전.</summary>
public sealed class MonitorCache
{
    /// <summary>캐시 키.</summary>
    public string CacheKey { get; set; } = "";

    /// <summary>버전.</summary>
    public long VersionNo { get; set; }

    /// <summary>마지막으로 올린 시각 (UTC).</summary>
    public DateTime UpdDt
    {
        get;
        set => field = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
}

/// <summary>배치 작업 상태.</summary>
public sealed class MonitorJob
{
    /// <summary>작업 ID.</summary>
    public string JobId { get; set; } = "";

    /// <summary>작업명.</summary>
    public string JobNm { get; set; } = "";

    /// <summary>사용 여부.</summary>
    public string UseYn { get; set; } = "Y";

    /// <summary>실행 중.</summary>
    public string RunningYn { get; set; } = "N";

    /// <summary>마지막 실행 (UTC).</summary>
    public DateTime? LastRunDt { get; set; }

    /// <summary>마지막 결과 코드.</summary>
    public string? LastResultCd { get; set; }

    /// <summary>마지막 결과 이름.</summary>
    public string? LastResultNm { get; set; }

    /// <summary>다음 실행 예정 (UTC).</summary>
    public DateTime? NextRunDt { get; set; }

    /// <summary>예정 시각이 10분 넘게 지났는데 실행되지 않음.</summary>
    public string OverdueYn { get; set; } = "N";

    /// <summary>제한 시간을 넘겨 멈춘 것으로 보임.</summary>
    public string StuckYn { get; set; } = "N";
}

/// <summary>상태별 건수 (메일 대기열, 레포트 요청).</summary>
public sealed class MonitorStatus
{
    /// <summary>상태 코드.</summary>
    public string StatCd { get; set; } = "";

    /// <summary>건수.</summary>
    public int Cnt { get; set; }

    /// <summary>가장 오래된 등록 시각 (UTC).</summary>
    public DateTime? OldestDt { get; set; }
}

/// <summary>최근 오류 한 건.</summary>
public sealed class MonitorError
{
    /// <summary>발생 시각 (UTC).</summary>
    public DateTime LogDt
    {
        get;
        set => field = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    /// <summary>추적 ID (사용자에게 보여 준 오류 화면의 번호).</summary>
    public string? TraceId { get; set; }

    /// <summary>사용자 ID.</summary>
    public string? UserId { get; set; }

    /// <summary>요청 경로.</summary>
    public string? RequestPath { get; set; }

    /// <summary>발생 위치.</summary>
    public string? Source { get; set; }

    /// <summary>오류 메시지 (300자까지).</summary>
    public string Message { get; set; } = "";
}

/// <summary>이 웹서버 프로세스 정보.</summary>
/// <param name="Version">애플리케이션 버전.</param>
/// <param name="Runtime">.NET 런타임.</param>
/// <param name="MachineName">서버 이름.</param>
/// <param name="StartedDt">프로세스 시작 시각 (UTC).</param>
/// <param name="WorkingSetMb">작업 집합 메모리(MB).</param>
/// <param name="ManagedMb">관리 힙 메모리(MB).</param>
/// <param name="ThreadCount">처리 중인 스레드 수.</param>
/// <param name="Gen2Collections">2세대 가비지 수집 횟수.</param>
public sealed record MonitorServer(string Version, string Runtime, string MachineName, DateTime StartedDt, long WorkingSetMb, long ManagedMb, int ThreadCount, int Gen2Collections);

/// <summary>운영 모니터링 응답.</summary>
public sealed class MonitorResult
{
    /// <summary>요약.</summary>
    public required MonitorSummary Summary { get; init; }

    /// <summary>캐시 버전.</summary>
    public required IReadOnlyList<MonitorCache> Caches { get; init; }

    /// <summary>배치 작업.</summary>
    public required IReadOnlyList<MonitorJob> Jobs { get; init; }

    /// <summary>메일 대기열 상태별 건수.</summary>
    public required IReadOnlyList<MonitorStatus> Mails { get; init; }

    /// <summary>레포트 요청 상태별 건수.</summary>
    public required IReadOnlyList<MonitorStatus> Reports { get; init; }

    /// <summary>최근 오류.</summary>
    public required IReadOnlyList<MonitorError> Errors { get; init; }

    /// <summary>이 서버 프로세스 정보.</summary>
    public required MonitorServer Server { get; init; }
}
