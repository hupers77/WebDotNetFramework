using Portal.Data;
using Portal.Framework.Web.Logging;
using Portal.IntegrationTests.Infrastructure;

namespace Portal.IntegrationTests.Log;

/// <summary>접근 기록 일괄 적재(TVP)와 해시 체인 점검 (설계서 9장, 10년 보관).</summary>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class AccessLogSpTests(DbFixture fixture)
{
    private sealed record VerifyResult(int CheckedCnt, long? BrokenLogId);

    [Fact]
    public async Task 일괄적재_건수반환_체인정상()
    {
        await using var db = await fixture.BeginAsync();
        var from = await db.ScalarAsync<DateTime>("SELECT SYSUTCDATETIME()");

        var saved = await WriteAsync(db, Entry(from, "/a", 1), Entry(from, "/b", 2), Entry(from, "/c", 3));

        Assert.Equal(3, saved.AffectedCnt);
        var paths = await db.ColumnAsync<string>("SELECT REQUEST_PATH FROM LOG.TB_ACCESS_LOG WHERE TRACE_ID = 'it-access' ORDER BY LOG_ID");
        Assert.Equal(["/a", "/b", "/c"], paths);
        var result = await VerifyAsync(db, from);
        Assert.Equal(3, result.CheckedCnt);
        Assert.Null(result.BrokenLogId);
    }

    [Fact]
    public async Task 여러번적재해도체인이이어짐_변조검출()
    {
        await using var db = await fixture.BeginAsync();
        var from = await db.ScalarAsync<DateTime>("SELECT SYSUTCDATETIME()");
        await WriteAsync(db, Entry(from, "/a", 1));
        await WriteAsync(db, Entry(from, "/b", 1), Entry(from, "/c", 2));
        Assert.Null((await VerifyAsync(db, from)).BrokenLogId);

        var target = await db.ScalarAsync<long>("SELECT MIN(LOG_ID) FROM LOG.TB_ACCESS_LOG WHERE TRACE_ID = 'it-access' AND REQUEST_PATH = '/b'");
        await db.ExecAsync("UPDATE LOG.TB_ACCESS_LOG SET STATUS_CODE = 500 WHERE LOG_ID = @ID", ("@ID", target));

        Assert.Equal(target, (await VerifyAsync(db, from)).BrokenLogId);
    }

    private static AccessLogEntry Entry(DateTime at, string path, int seq) => new()
    {
        SeqNo = seq,
        LogDt = at,
        UserId = null,
        ProgramId = "DSH_HOME",
        FuncCd = "READ",
        HttpMethod = "GET",
        RequestPath = path,
        StatusCode = 200,
        ElapsedMs = 12,
        ClientIp = "10.0.0.1",
        TraceId = "it-access",
    };

    private static Task<SaveResult> WriteAsync(TestDb db, params AccessLogEntry[] rows) =>
        db.Sp.ExecuteAsync("LOG.USP_ACCESS_LOG_WRITE", new { P_ROWS = Tvp.From("LOG.TVP_ACCESS_LOG", rows) }, TestContext.Current.CancellationToken);

    private static async Task<VerifyResult> VerifyAsync(TestDb db, DateTime from) =>
        (await db.Sp.QuerySingleAsync<VerifyResult>("LOG.USP_ACCESS_LOG_VERIFY_EXEC",
            new { P_FROM_DT = from.AddSeconds(-1), P_TO_DT = from.AddHours(1) }, TestContext.Current.CancellationToken))!;
}
