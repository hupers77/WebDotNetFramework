using Portal.Core.Errors;
using Portal.IntegrationTests.Infrastructure;
using Portal.Web.Areas.Rpt.Models;
using Portal.Web.Areas.Rpt.Services;

namespace Portal.IntegrationTests.Fw;

/// <summary>레포트 정의 SP·표: 초기 레포트, 레포트나 프로그램이 사용 안 함이면 없음, 데이터 SP 이름 제약, 레포트가 있는 프로그램은 지우지 않음.</summary>
/// <remarks>SP 오류는 테스트 트랜잭션도 롤백하므로 오류 확인은 각 테스트의 마지막 호출로 둔다.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class ReportSpTests(DbFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<ReportDefinition?> GetAsync(TestDb db, string id) =>
        db.Sp.QuerySingleAsync<ReportDefinition>(ReportService.GetSp, new { P_REPORT_ID = id }, Ct);

    /// <summary>시험용 프로그램(메뉴·경로 없음)과 레포트.</summary>
    private static Task<int> SeedAsync(TestDb db) => db.ExecAsync("""
        INSERT INTO FW.TB_PROGRAM (PROGRAM_ID, MODULE_CD, PROGRAM_NM, URL, PII_YN) VALUES ('IT_RPT_X', 'RPT', N'시험 레포트', '/Rpt/Viewer/Index/IT_RPT_X', 'Y');
        INSERT INTO FW.TB_REPORT (REPORT_ID, DATA_SP) VALUES ('IT_RPT_X', 'LOG.USP_RPT_ACCESS_MONTHLY_GET_LIST');
        """);

    [Fact]
    public async Task 초기레포트_월별접속기록점검()
    {
        await using var db = await fixture.BeginAsync();

        var def = await GetAsync(db, "RPT_LOG_MONTHLY");

        Assert.NotNull(def);
        Assert.Equal(("LOG.USP_RPT_ACCESS_MONTHLY_GET_LIST", "AccessMonthly", true, "Y", "N"), (def.DataSp, def.TemplateView, def.Landscape, def.ApprovalYn, def.PiiYn));
        Assert.Equal("P_MONTH", Assert.Single(ReportParams.Parse(def.ParamsJson)).Name);
    }

    [Fact]
    public async Task 기본값_개인정보는프로그램기준_사용안함이면없음()
    {
        await using var db = await fixture.BeginAsync();
        await SeedAsync(db);

        var def = await GetAsync(db, "IT_RPT_X");
        await db.ExecAsync("UPDATE FW.TB_REPORT SET USE_YN = 'N' WHERE REPORT_ID = 'IT_RPT_X'");
        var reportOff = await GetAsync(db, "IT_RPT_X");
        await db.ExecAsync("UPDATE FW.TB_REPORT SET USE_YN = 'Y' WHERE REPORT_ID = 'IT_RPT_X'; UPDATE FW.TB_PROGRAM SET USE_YN = 'N' WHERE PROGRAM_ID = 'IT_RPT_X'");
        var programOff = await GetAsync(db, "IT_RPT_X");

        Assert.Equal((null, "P", "N", "N", 5000, "Y"), (def!.TemplateView, def.OrientCd, def.ApprovalYn, def.TotalYn, def.MaxRows, def.PiiYn));
        Assert.Null(reportOff);
        Assert.Null(programOff);
        Assert.Null(await GetAsync(db, "IT_RPT_NONE"));
    }

    [Theory]
    [InlineData("FW.USP_USER_GET_LIST")]
    [InlineData("LOG.USP_RPT_X_EXEC")]
    public async Task 데이터SP는레포트SP형식만(string dataSp)
    {
        await using var db = await fixture.BeginAsync();
        await SeedAsync(db);

        await Assert.ThrowsAnyAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            db.ExecAsync("UPDATE FW.TB_REPORT SET DATA_SP = @SP WHERE REPORT_ID = 'IT_RPT_X'", ("@SP", dataSp)));
    }

    [Fact]
    public async Task 레포트가있는프로그램은_지우지않음()
    {
        await using var db = await fixture.BeginAsync();
        await SeedAsync(db);
        var rowVer = await db.ScalarAsync<byte[]>("SELECT ROW_VER FROM FW.TB_PROGRAM WHERE PROGRAM_ID = 'IT_RPT_X'");

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync("FW.USP_PROGRAM_DEL", new { P_PROGRAM_ID = "IT_RPT_X", P_ROW_VER = rowVer }, Ct));

        Assert.Equal("MSG.PROGRAM_IN_USE", ex.MessageKey);
    }
}
