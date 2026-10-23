using System.Globalization;
using Microsoft.Extensions.Options;
using Portal.Core.Scheduling;
using Portal.Data;
using Portal.Infrastructure.Scheduling;

namespace Portal.Infrastructure.Logging;

/// <summary>
/// 배치 작업 <c>JOB_LOG_INTEGRITY</c>(매월 1일 06:00): 지난달(스케줄러 시간대 기준)의 로그인·접근·데이터 변경·개인정보 접속기록
/// 해시 체인을 다시 계산해 위·변조와 삭제를 찾는다. 점검 SP 한 번이 오래 걸리지 않게 6시간씩 나눠 부르고,
/// 점검 SP가 구간 경계의 앞 행까지 맞춰 보므로 나눠도 빠지는 행이 없다.
/// 깨진 곳이 있으면 네 로그를 모두 본 뒤 실패로 끝내 관리자에게 알린다(테이블과 처음 깨진 LOG_ID만 남긴다).
/// </summary>
public sealed class LogIntegrityJob(ISpExecutor sp, IOptions<SchedulerOptions> options, TimeProvider time) : IScheduledJob
{
    /// <summary>한 번에 점검하는 시간.</summary>
    internal static readonly TimeSpan Window = TimeSpan.FromHours(6);

    /// <summary>점검할 로그 (테이블, 점검 SP, 결과에 쓰는 이름).</summary>
    internal static readonly (string Table, string Sp, string Label)[] Logs =
    [
        ("LOG.TB_LOGIN_LOG", "LOG.USP_LOGIN_LOG_VERIFY_EXEC", "로그인"),
        ("LOG.TB_ACCESS_LOG", "LOG.USP_ACCESS_LOG_VERIFY_EXEC", "접근"),
        ("LOG.TB_AUDIT_LOG", "LOG.USP_AUDIT_VERIFY_EXEC", "데이터 변경"),
        ("LOG.TB_PII_ACCESS_LOG", "LOG.USP_PII_ACCESS_LOG_VERIFY_EXEC", "개인정보 접속"),
    ];

    private readonly TimeZoneInfo _timeZone = JobScheduler.ResolveTimeZone(options.Value.TimeZone);

    /// <inheritdoc />
    public string Name => "LogIntegrity";

    /// <inheritdoc />
    public async Task<JobRunResult> RunAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        var (month, from, to) = LastMonth();
        var counts = new List<string>();
        var broken = new List<string>();
        var total = 0;
        foreach (var (table, spName, label) in Logs)
        {
            var (checkedCnt, brokenLogId) = await VerifyAsync(spName, from, to, cancellationToken);
            total += checkedCnt;
            counts.Add(string.Create(CultureInfo.InvariantCulture, $"{label} {checkedCnt:N0}"));
            if (brokenLogId is { } id)
            {
                broken.Add(string.Create(CultureInfo.InvariantCulture, $"{table} LOG_ID {id}"));
            }
        }

        if (broken.Count > 0)
        {
            throw new InvalidOperationException($"LOG_INTEGRITY_BROKEN: {month} {string.Join(", ", broken)}");
        }

        return JobRunResult.Done(total, $"{month} 이상 없음 ({string.Join(", ", counts)})");
    }

    /// <summary>지난달 (yyyy-MM, 시작 UTC, 끝 UTC). 달의 경계는 스케줄러 시간대 0시다.</summary>
    internal (string Month, DateTime From, DateTime To) LastMonth()
    {
        var local = TimeZoneInfo.ConvertTime(time.GetUtcNow(), _timeZone);
        var thisMonth = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var lastMonth = thisMonth.AddMonths(-1);
        return (lastMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            TimeZoneInfo.ConvertTimeToUtc(lastMonth, _timeZone),
            TimeZoneInfo.ConvertTimeToUtc(thisMonth, _timeZone));
    }

    private async Task<(int Checked, long? BrokenLogId)> VerifyAsync(string spName, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var checkedCnt = 0;
        long? brokenLogId = null;
        for (var start = from; start < to; start += Window)
        {
            var end = start + Window < to ? start + Window : to;
            var row = await sp.QuerySingleAsync<VerifyRow>(spName, new { P_FROM_DT = start, P_TO_DT = end }, cancellationToken)
                ?? throw new InvalidOperationException($"LOG_VERIFY_NO_RESULT: {spName}");
            checkedCnt += row.CheckedCnt;
            brokenLogId ??= row.BrokenLogId; // 앞 구간부터 보므로 처음 깨진 곳
        }

        return (checkedCnt, brokenLogId);
    }

    /// <summary>점검 SP 결과.</summary>
    internal sealed class VerifyRow
    {
        public int CheckedCnt { get; set; }

        public long? BrokenLogId { get; set; }
    }
}
