namespace Portal.Core.Scheduling;

/// <summary>
/// 클래스로 만드는 배치 작업 (설계서 13장 스케줄러). 구현을 DI에 <c>IScheduledJob</c>으로 등록하고,
/// 스케줄 관리(<c>TB_JOB</c>)에 <c>JOB_TYPE_CD = CLASS</c>, <c>JOB_TARGET = </c><see cref="Name"/>으로 주기를 등록한다.
/// DB만 다루는 작업은 클래스 대신 <c>FW.USP_JOB_..._EXEC</c> SP로 만들고 <c>JOB_TYPE_CD = SP</c>로 등록한다.
/// </summary>
/// <remarks>
/// 실행할 때마다 새 DI 범위에서 만들어진다. 같은 작업은 여러 Worker에서 동시에 돌지 않는다(DB 잠금).
/// 제한 시간(<c>TIMEOUT_MIN</c>)이 지나거나 서비스가 멈추면 취소 토큰이 취소되므로, 긴 작업은 묶음마다 확인한다.
/// </remarks>
public interface IScheduledJob
{
    /// <summary><c>TB_JOB.JOB_TARGET</c>에 적는 이름 (예: <c>MailSend</c>).</summary>
    string Name { get; }

    /// <summary>작업을 한 번 실행한다. 예외를 던지면 실패(FAILED)로 기록되고 관리자에게 알린다.</summary>
    Task<JobRunResult> RunAsync(JobRunContext context, CancellationToken cancellationToken);
}

/// <summary>실행 정보.</summary>
/// <param name="JobId">작업 ID.</param>
/// <param name="RunTypeCd">SCHEDULE(예약) / MANUAL(수동).</param>
/// <param name="RunUserId">수동 실행을 요청한 사용자.</param>
public sealed record JobRunContext(string JobId, string RunTypeCd, string? RunUserId);

/// <summary>실행 결과. 이력(<c>TB_JOB_HIST</c>)에 남는다. 메시지에는 개인정보를 넣지 않는다.</summary>
/// <param name="ProcessedCount">처리 건수.</param>
/// <param name="Message">결과 메모.</param>
/// <param name="Skipped">할 일이 없어 건너뛰었으면 true (SKIPPED).</param>
public sealed record JobRunResult(int? ProcessedCount = null, string? Message = null, bool Skipped = false)
{
    /// <summary>처리 건수만 남기는 성공.</summary>
    public static JobRunResult Done(int processedCount, string? message = null) => new(processedCount, message);

    /// <summary>건너뜀 (예: 기능이 꺼져 있음).</summary>
    public static JobRunResult Skip(string message) => new(null, message, true);
}
