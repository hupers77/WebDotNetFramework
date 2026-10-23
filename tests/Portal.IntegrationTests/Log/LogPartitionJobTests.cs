using Portal.IntegrationTests.Infrastructure;

namespace Portal.IntegrationTests.Log;

/// <summary>
/// 로그 파티션 작업 SP(<c>LOG.USP_JOB_LOG_PARTITION_EXEC</c>): 2개월 뒤까지 월 경계 추가, 보관 기간이 지난 월 파티션 비우기
/// (오류 로그는 LOG.ERROR_RETENTION_DAYS, 나머지는 LOG.RETENTION_YEARS), 설정이 0이면 지우지 않음.
/// </summary>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class LogPartitionJobTests(DbFixture fixture)
{
    private const string Sp = "LOG.USP_JOB_LOG_PARTITION_EXEC";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<ProcessedRow?> RunAsync(TestDb db) => db.Sp.QuerySingleAsync<ProcessedRow>(Sp, null, Ct);

    private static Task<int> ErrorRowsAsync(TestDb db, string message) =>
        db.ScalarAsync<int>("SELECT COUNT(*) FROM LOG.TB_ERROR_LOG WHERE MESSAGE = @M", ("@M", message));

    /// <summary>오류 로그 행을 오늘로부터 몇 달 전 시각으로 넣는다.</summary>
    private static Task<int> InsertErrorAsync(TestDb db, string message, int monthsAgo) =>
        db.ExecAsync("INSERT INTO LOG.TB_ERROR_LOG (LOG_DT, MESSAGE) VALUES (DATEADD(MONTH, -@N, SYSUTCDATETIME()), @M)", ("@N", monthsAgo), ("@M", message));

    [Fact]
    public async Task 두달뒤1일까지경계를만들고_다시돌면추가하지않는다()
    {
        await using var db = await fixture.BeginAsync();
        await db.ExecAsync("UPDATE FW.TB_CONFIG SET CONFIG_VAL = '0' WHERE CONFIG_KEY IN ('LOG.RETENTION_YEARS', 'LOG.ERROR_RETENTION_DAYS')");

        await RunAsync(db);
        var second = await RunAsync(db);

        Assert.Equal(0, second!.ProcessedCnt);
        Assert.Equal(1, await db.ScalarAsync<int>("""
            SELECT COUNT(*)
              FROM sys.partition_range_values R
              JOIN sys.partition_functions F ON F.function_id = R.function_id AND F.name = 'PF_LOG_MONTH'
             WHERE CAST(R.value AS DATETIME2(3)) = DATEADD(MONTH, 2, DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1))
            """));
    }

    [Fact]
    public async Task 보관기간지난오류로그파티션만비우고_최근행과다른로그는남긴다()
    {
        await using var db = await fixture.BeginAsync();
        await db.ExecAsync("UPDATE FW.TB_CONFIG SET CONFIG_VAL = '30' WHERE CONFIG_KEY = 'LOG.ERROR_RETENTION_DAYS'; UPDATE FW.TB_CONFIG SET CONFIG_VAL = '10' WHERE CONFIG_KEY = 'LOG.RETENTION_YEARS';");
        await InsertErrorAsync(db, "it.part.old", monthsAgo: 3);
        await InsertErrorAsync(db, "it.part.new", monthsAgo: 0);
        var auditBefore = await db.ScalarAsync<int>("SELECT COUNT(*) FROM LOG.TB_AUDIT_LOG");

        await RunAsync(db);

        Assert.Equal(0, await ErrorRowsAsync(db, "it.part.old"));
        Assert.Equal(1, await ErrorRowsAsync(db, "it.part.new"));
        Assert.Equal(auditBefore, await db.ScalarAsync<int>("SELECT COUNT(*) FROM LOG.TB_AUDIT_LOG"));
    }

    [Fact]
    public async Task 보관설정이0이면_지우지않는다()
    {
        await using var db = await fixture.BeginAsync();
        await db.ExecAsync("UPDATE FW.TB_CONFIG SET CONFIG_VAL = '0' WHERE CONFIG_KEY = 'LOG.ERROR_RETENTION_DAYS'");
        await InsertErrorAsync(db, "it.part.keep", monthsAgo: 3);

        await RunAsync(db);

        Assert.Equal(1, await ErrorRowsAsync(db, "it.part.keep"));
    }

    private sealed record ProcessedRow(int ProcessedCnt);
}
