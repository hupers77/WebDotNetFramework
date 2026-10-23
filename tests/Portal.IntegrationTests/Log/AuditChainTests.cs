using Portal.IntegrationTests.Infrastructure;

namespace Portal.IntegrationTests.Log;

/// <summary>감사 로그 해시 체인: 적재와 위·변조 검출(설계서 7장).</summary>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class AuditChainTests(DbFixture fixture)
{
    private sealed record VerifyResult(int CheckedCnt, long? BrokenLogId);

    [Fact]
    public async Task 적재후점검_깨진행없음()
    {
        await using var db = await fixture.BeginAsync();
        var from = await db.ScalarAsync<DateTime>("SELECT SYSUTCDATETIME()");

        await WriteAsync(db, "K1", null, """{"a":1}""");
        await WriteAsync(db, "K2", """{"a":1}""", """{"a":2}""");
        await WriteAsync(db, "K3", """{"a":2}""", null);

        var result = await VerifyAsync(db, from);

        Assert.Equal(3, result.CheckedCnt);
        Assert.Null(result.BrokenLogId);
    }

    [Fact]
    public async Task 내용변조_해당행검출()
    {
        await using var db = await fixture.BeginAsync();
        var from = await db.ScalarAsync<DateTime>("SELECT SYSUTCDATETIME()");
        await WriteAsync(db, "K1", null, """{"amount":100}""");
        await WriteAsync(db, "K2", null, """{"amount":200}""");
        var target = await db.ScalarAsync<long>("SELECT MAX(LOG_ID) FROM LOG.TB_AUDIT_LOG WHERE ROW_KEY = 'K1' AND TABLE_NM = 'IT.TABLE'");

        await db.ExecAsync("UPDATE LOG.TB_AUDIT_LOG SET AFTER_JSON = N'{\"amount\":1}' WHERE LOG_ID = @ID", ("@ID", target));

        Assert.Equal(target, (await VerifyAsync(db, from)).BrokenLogId);
    }

    [Fact]
    public async Task 중간행삭제_다음행검출()
    {
        await using var db = await fixture.BeginAsync();
        var from = await db.ScalarAsync<DateTime>("SELECT SYSUTCDATETIME()");
        await WriteAsync(db, "K1", null, "1");
        await WriteAsync(db, "K2", null, "2");
        await WriteAsync(db, "K3", null, "3");
        var ids = await db.ColumnAsync<long>("SELECT LOG_ID FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'IT.TABLE' ORDER BY LOG_ID");

        await db.ExecAsync("DELETE FROM LOG.TB_AUDIT_LOG WHERE LOG_ID = @ID", ("@ID", ids[^2]));

        Assert.Equal(ids[^1], (await VerifyAsync(db, from)).BrokenLogId);
    }

    [Fact]
    public async Task 구간경계앞행삭제_다음구간첫행검출()
    {
        await using var db = await fixture.BeginAsync();
        await WriteAsync(db, "K1", null, "1");
        await WriteAsync(db, "K2", null, "2");
        await WriteAsync(db, "K3", null, "3");
        var rows = await db.ColumnAsync<long>("SELECT LOG_ID FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'IT.TABLE' ORDER BY LOG_ID");
        var lastAt = await db.ScalarAsync<DateTime>("SELECT LOG_DT FROM LOG.TB_AUDIT_LOG WHERE LOG_ID = @ID", ("@ID", rows[^1]));

        await db.ExecAsync("DELETE FROM LOG.TB_AUDIT_LOG WHERE LOG_ID = @ID", ("@ID", rows[^2]));

        // 마지막 행만 들어가는 구간으로 점검해도 바로 앞 구간의 행과 이어지는지 본다 (배치는 기간을 잘라 점검한다)
        var result = (await db.Sp.QuerySingleAsync<VerifyResult>("LOG.USP_AUDIT_VERIFY_EXEC",
            new { P_FROM_DT = lastAt, P_TO_DT = lastAt.AddHours(1) }, TestContext.Current.CancellationToken))!;
        Assert.Equal(rows[^1], result.BrokenLogId);
    }

    private static Task<Portal.Data.SaveResult> WriteAsync(TestDb db, string rowKey, string? before, string? after) =>
        db.Sp.ExecuteAsync("LOG.USP_AUDIT_WRITE", new
        {
            P_TABLE_NM = "IT.TABLE",
            P_ACTION_CD = "U",
            P_ROW_KEY = rowKey,
            P_BEFORE_JSON = before,
            P_AFTER_JSON = after,
        }, TestContext.Current.CancellationToken);

    private static async Task<VerifyResult> VerifyAsync(TestDb db, DateTime from) =>
        (await db.Sp.QuerySingleAsync<VerifyResult>("LOG.USP_AUDIT_VERIFY_EXEC",
            new { P_FROM_DT = from.AddSeconds(-1), P_TO_DT = from.AddHours(1) }, TestContext.Current.CancellationToken))!;
}
