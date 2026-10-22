using Portal.Core.Errors;
using Portal.Testing;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.UnitTests.Sys;

/// <summary>스케줄 관리 Service: 주기 식은 Worker와 같은 규칙으로 검사하고 공백을 맞춰 저장한다.</summary>
public sealed class JobAdminServiceTests
{
    private readonly FakeSpExecutor _sp = new();

    private JobAdminService Service => new(_sp);

    private static JobSaveRequest Request(string? cron, string? desc = null) =>
        new() { JobId = "JOB_X", EditVer = 3, CronExpr = cron, TimeoutMin = 30, UseYn = "Y", Description = desc };

    [Fact]
    public async Task 저장_주기공백을맞추고_설명은다듬는다()
    {
        await Service.SaveAsync(Request("  0   5 * *\t*  ", "  매일  "));

        var p = _sp.LastCall(JobAdminService.SaveSp).Parameters;
        Assert.Equal(("JOB_X", (object?)3, (object?)"0 5 * * *", (object?)30, (object?)"Y", (object?)"매일"),
            ((string)p["P_JOB_ID"]!, p["P_EDIT_VER"], p["P_CRON_EXPR"], p["P_TIMEOUT_MIN"], p["P_USE_YN"], p["P_DESCRIPTION"]));
    }

    [Fact]
    public async Task 저장_빈주기는_수동실행만()
    {
        await Service.SaveAsync(Request("   ", "  "));

        var p = _sp.LastCall(JobAdminService.SaveSp).Parameters;
        Assert.Null(p["P_CRON_EXPR"]);
        Assert.Null(p["P_DESCRIPTION"]);
    }

    [Theory]
    [InlineData("every day")]
    [InlineData("0 5 * *")]
    [InlineData("61 * * * *")]
    public async Task 저장_틀린주기는_SP를부르지않고오류(string cron)
    {
        var ex = await Assert.ThrowsAsync<BizException>(() => Service.SaveAsync(Request(cron)));

        Assert.Equal(JobMessageKeys.CronInvalid, ex.MessageKey);
        Assert.Empty(_sp.Calls);
    }

    [Fact]
    public async Task 목록_검색어를다듬어넘긴다()
    {
        await Service.GetListAsync(new JobSearch { Keyword = "  mail " });

        Assert.Equal("mail", _sp.LastCall(JobAdminService.ListSp).Parameters["P_KEYWORD"]);
    }
}
