using Microsoft.Extensions.Options;
using Portal.Core.Scheduling;
using Portal.Infrastructure.Logging;
using Portal.Infrastructure.Scheduling;
using Portal.Testing;

namespace Portal.UnitTests.Infrastructure;

/// <summary>로그 무결성 점검 작업: 지난달 경계(한국 시간), 6시간씩 빈틈없이 나눠 점검, 건수 합계, 깨진 곳이 있으면 네 로그를 다 본 뒤 실패.</summary>
public sealed class LogIntegrityJobTests
{
    private static readonly JobRunContext Context = new("JOB_LOG_INTEGRITY", "SCHEDULE", null);

    // 2026-11-01 06:00 한국 시간 (예약 실행 시각)
    private static readonly DateTimeOffset RunAt = new(2026, 10, 31, 21, 0, 0, TimeSpan.Zero);
    private static readonly DateTime From = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 10, 31, 15, 0, 0, DateTimeKind.Utc);
    private readonly FakeSpExecutor _sp = new();

    private LogIntegrityJob Job() => new(_sp, Options.Create(new SchedulerOptions()), new FakeTimeProvider(RunAt));

    private void ReturnsForAll(Func<string, IReadOnlyDictionary<string, object?>, LogIntegrityJob.VerifyRow> factory)
    {
        foreach (var (_, spName, _) in LogIntegrityJob.Logs)
        {
            _sp.Returns(spName, p => factory(spName, p));
        }
    }

    [Fact]
    public void 지난달은_스케줄러시간대0시기준()
    {
        Assert.Equal(("2026-10", From, To), Job().LastMonth());
    }

    [Fact]
    public async Task 여섯시간씩빈틈없이나눠점검하고_건수를더한다()
    {
        ReturnsForAll((_, _) => new LogIntegrityJob.VerifyRow { CheckedCnt = 2 });

        var result = await Job().RunAsync(Context, CancellationToken.None);

        var windows = 31 * 24 / 6;
        foreach (var (_, spName, _) in LogIntegrityJob.Logs)
        {
            var calls = _sp.Calls.Where(c => c.SpName == spName).Select(c => ((DateTime)c.Parameters["P_FROM_DT"]!, (DateTime)c.Parameters["P_TO_DT"]!)).ToList();
            Assert.Equal(windows, calls.Count);
            Assert.Equal(From, calls[0].Item1);
            Assert.Equal(To, calls[^1].Item2);
            Assert.All(calls.Zip(calls.Skip(1)), pair => Assert.Equal(pair.First.Item2, pair.Second.Item1)); // 구간 사이 빈틈 없음
        }

        Assert.Equal(4 * windows * 2, result.ProcessedCount);
        Assert.StartsWith("2026-10 이상 없음", result.Message, StringComparison.Ordinal);
        Assert.False(result.Skipped);
    }

    [Fact]
    public async Task 깨진곳이있으면_네로그를다본뒤_테이블과처음깨진LOG_ID로실패()
    {
        ReturnsForAll((spName, p) => new LogIntegrityJob.VerifyRow
        {
            CheckedCnt = 1,
            BrokenLogId = spName == "LOG.USP_AUDIT_VERIFY_EXEC" && (DateTime)p["P_FROM_DT"]! >= From.AddDays(10) ? 77 + ((DateTime)p["P_FROM_DT"]!).Day : null,
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Job().RunAsync(Context, CancellationToken.None));

        Assert.Equal("LOG_INTEGRITY_BROKEN: 2026-10 LOG.TB_AUDIT_LOG LOG_ID 87", ex.Message); // 10월 10일 15:00 UTC 구간이 처음
        Assert.Contains(_sp.Calls, c => c.SpName == "LOG.USP_PII_ACCESS_LOG_VERIFY_EXEC");
    }
}
