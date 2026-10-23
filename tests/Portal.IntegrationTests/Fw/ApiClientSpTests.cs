using Portal.Data;
using Portal.Framework.Web.Api;
using Portal.IntegrationTests.Infrastructure;

namespace Portal.IntegrationTests.Fw;

/// <summary>외부 API SP: 클라이언트 조회(사용 기한 판단)와 API 기록 적재.</summary>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class ApiClientSpTests(DbFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task 사용중지와_기한만료는_ACTIVE_YN이_N()
    {
        await using var db = await fixture.BeginAsync();
        await db.ExecAsync("""
            INSERT INTO FW.TB_API_CLIENT (CLIENT_ID, CLIENT_NM, API_CODES, USE_YN, EXPIRE_DT) VALUES
              ('t-on', N'사용', 'PING', 'Y', NULL),
              ('t-last-day', N'기한 마지막날', 'PING', 'Y', CONVERT(DATE, SYSUTCDATETIME())),
              ('t-expired', N'기한 지남', 'PING', 'Y', DATEADD(DAY, -1, CONVERT(DATE, SYSUTCDATETIME()))),
              ('t-off', N'중지', 'PING', 'N', NULL)
            """);

        async Task<string> Active(string id) =>
            (await db.Sp.QuerySingleAsync<ApiClientStore.ApiClientRow>(ApiClientStore.GetSp, new { P_CLIENT_ID = id }, Ct))!.ActiveYn;

        Assert.Equal(["Y", "Y", "N", "N"], [await Active("t-on"), await Active("t-last-day"), await Active("t-expired"), await Active("t-off")]);
        Assert.Null(await db.Sp.QuerySingleAsync<ApiClientStore.ApiClientRow>(ApiClientStore.GetSp, new { P_CLIENT_ID = "t-none" }, Ct));
    }

    [Fact]
    public async Task 비밀값은_해시와_salt가_함께_있거나_함께_없어야_한다()
    {
        await using var db = await fixture.BeginAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => db.ExecAsync(
            "INSERT INTO FW.TB_API_CLIENT (CLIENT_ID, CLIENT_NM, SECRET_HASH) VALUES ('t-half', N'반쪽', 0x01)"));
    }

    [Fact]
    public async Task API기록은_일괄_적재되고_방향이_잘못된_행은_건너뛴다()
    {
        await using var db = await fixture.BeginAsync();

        var saved = await db.Sp.ExecuteAsync(ApiLogWriter.WriteSp, new
        {
            P_ROWS_JSON = """
                [{"logDt":"2026-10-23T01:02:03.456","directionCd":"IN","clientId":"t-on","httpMethod":"GET","url":"/api/v1/ping","statusCode":200,"elapsedMs":12,"traceId":"x"},
                 {"logDt":"2026-10-23T01:02:04.000","directionCd":"XX","httpMethod":"GET","url":"/bad"}]
                """,
        }, Ct);

        Assert.Equal(1, saved.AffectedCnt);
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM LOG.TB_API_LOG WHERE CLIENT_ID = 't-on' AND URL = '/api/v1/ping' AND STATUS_CODE = 200"));
    }
}
