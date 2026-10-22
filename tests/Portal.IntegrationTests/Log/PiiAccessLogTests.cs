using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Framework.Web.Logging;
using Portal.IntegrationTests.Infrastructure;

namespace Portal.IntegrationTests.Log;

/// <summary>개인정보 접속기록: 사유 필수, 해시 체인 적재와 위·변조 검출.</summary>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class PiiAccessLogTests(DbFixture fixture)
{
    private const string Program = "SYS_USER_MGT";

    private sealed record VerifyResult(int CheckedCnt, long? BrokenLogId);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task 적재_사용자와사유가남고_점검에서깨진행없음()
    {
        await using var db = await fixture.BeginAsync();
        var from = await db.ScalarAsync<DateTime>("SELECT SYSUTCDATETIME()");
        var log = new PiiAccessLog(db.Sp);

        await log.WriteAsync(Program, PiiActions.View, "{\"cnt\":3}", null, Ct);
        await log.WriteAsync(Program, PiiActions.Download, "{\"cnt\":3}", "  감사 자료  ", Ct);

        var row = await db.ScalarAsync<string>("SELECT TOP (1) CONCAT(USER_ID, '|', ACTION_CD, '|', REASON) FROM LOG.TB_PII_ACCESS_LOG ORDER BY LOG_ID DESC");
        Assert.Equal($"{db.User.UserId}|DOWNLOAD|감사 자료", row);
        var result = await VerifyAsync(db, from);
        Assert.Equal((2, (long?)null), (result.CheckedCnt, result.BrokenLogId));
    }

    [Theory]
    [InlineData(PiiActions.Download, null)]
    [InlineData(PiiActions.Unmask, " a ")]
    public async Task 다운로드와마스킹해제는_사유2자이상(string action, string? reason)
    {
        await using var db = await fixture.BeginAsync();

        var ex = await Assert.ThrowsAsync<BizException>(() => new PiiAccessLog(db.Sp).WriteAsync(Program, action, null, reason, Ct));

        Assert.Equal(MessageKeys.PiiReasonRequired, ex.MessageKey);
    }

    [Theory]
    [InlineData("EXPORT", Program)]
    [InlineData(PiiActions.View, "NO_SUCH_PROGRAM")]
    public async Task 모르는행위나프로그램은_거부(string action, string program)
    {
        await using var db = await fixture.BeginAsync();

        var ex = await Assert.ThrowsAsync<BizException>(() => new PiiAccessLog(db.Sp).WriteAsync(program, action, null, "사유", Ct));

        Assert.Equal(MessageKeys.InvalidInput, ex.MessageKey);
    }

    [Fact]
    public async Task 사유변조_해당행검출()
    {
        await using var db = await fixture.BeginAsync();
        var from = await db.ScalarAsync<DateTime>("SELECT SYSUTCDATETIME()");
        var log = new PiiAccessLog(db.Sp);
        await log.WriteAsync(Program, PiiActions.Download, null, "업무 확인", Ct);
        await log.WriteAsync(Program, PiiActions.Download, null, "업무 확인", Ct);
        var target = await db.ScalarAsync<long>("SELECT MIN(LOG_ID) FROM LOG.TB_PII_ACCESS_LOG WHERE LOG_DT >= @F", ("@F", from.AddSeconds(-1)));

        await db.ExecAsync("UPDATE LOG.TB_PII_ACCESS_LOG SET REASON = N'다른 사유' WHERE LOG_ID = @ID", ("@ID", target));

        Assert.Equal(target, (await VerifyAsync(db, from)).BrokenLogId);
    }

    private static async Task<VerifyResult> VerifyAsync(TestDb db, DateTime from) =>
        (await db.Sp.QuerySingleAsync<VerifyResult>("LOG.USP_PII_ACCESS_LOG_VERIFY_EXEC",
            new { P_FROM_DT = from.AddSeconds(-1), P_TO_DT = from.AddHours(1) }, Ct))!;
}
