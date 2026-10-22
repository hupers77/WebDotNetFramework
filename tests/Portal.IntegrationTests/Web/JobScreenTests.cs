using System.Net;
using Portal.Core.Results;
using Portal.Data;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>스케줄 관리 화면: 권한별 버튼(저장 UPDATE, 지금 실행 EXEC), 권한 없는 요청 차단, 주기 검사.</summary>
public sealed class JobScreenTests : IClassFixture<PortalWebFactory>
{
    private const string Program = "SYS_JOB_MGT";
    private const string Home = "DSH_HOME:READ";
    private static readonly string[] AllFuncs = [Home, $"{Program}:READ", $"{Program}:UPDATE", $"{Program}:EXEC"];
    private readonly PortalWebFactory _web;

    public JobScreenTests(PortalWebFactory web)
    {
        _web = web;
        web.Sp.Returns(JobAdminService.ListSp, new PageResult<JobDto>
        {
            Items = [new() { JobId = "JOB_MAIL_SEND", JobNm = "메일 발송", JobTypeCd = "CLASS", JobTarget = "MailSend", CronExpr = "* * * * *", TimeoutMin = 10, EditVer = 1 }],
            TotalCount = 1,
            PageNo = 1,
            PageSize = JobAdminService.MaxRows,
        });
        web.Sp.Returns(JobAdminService.HistorySp, new PageResult<JobHistDto> { Items = [], TotalCount = 0, PageNo = 1, PageSize = JobAdminService.HistoryPageSize });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> PageAsync(string token)
    {
        var response = await _web.NewClient(token).GetAsync("/Sys/Job?_layout=tab", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private async Task<HttpResponseMessage> PostAsync(string token, string action, object body)
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(token), "/");
        var request = Ajax(HttpMethod.Post, $"/Sys/Job/{action}", body);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await _web.NewClient(token, cookie).SendAsync(request, Ct);
    }

    private static object Save(string? cron) => new { jobId = "JOB_MAIL_SEND", editVer = 1, cronExpr = cron, timeoutMin = 10, useYn = "Y" };

    [Fact]
    public async Task 화면_권한별로_저장과지금실행버튼()
    {
        var all = await PageAsync(_web.SignIn("job.admin", AllFuncs));
        var reader = await PageAsync(_web.SignIn("job.reader", [Home, $"{Program}:READ"]));

        Assert.Contains("id=\"job-save\"", all, StringComparison.Ordinal);
        Assert.Contains("id=\"job-run\"", all, StringComparison.Ordinal);
        Assert.Contains("js/sys/job.js", all, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"job-save\"", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"job-run\"", reader, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 조회권한없음_화면목록이력모두403()
    {
        var token = _web.SignIn("job.none", [Home]);

        var page = await _web.NewClient(token).GetAsync("/Sys/Job?_layout=tab", Ct);
        var list = await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Sys/Job/List"), Ct);
        var history = await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Sys/Job/History?jobId=JOB_MAIL_SEND"), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, page.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, history.StatusCode);
    }

    [Fact]
    public async Task 저장과실행_권한이없으면403_SP를부르지않는다()
    {
        var token = _web.SignIn("job.reader2", [Home, $"{Program}:READ"]);
        var before = _web.Sp.Calls.Count(c => c.SpName is JobAdminService.SaveSp or JobAdminService.RunSp);

        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(token, "Save", Save("0 5 * * *"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(token, "Run", new { jobId = "JOB_MAIL_SEND" })).StatusCode);
        Assert.Equal(before, _web.Sp.Calls.Count(c => c.SpName is JobAdminService.SaveSp or JobAdminService.RunSp));
    }

    [Fact]
    public async Task 저장_틀린주기는_메시지키로거부()
    {
        var response = await PostAsync(_web.SignIn("job.saver", AllFuncs), "Save", Save("every minute"));

        var error = (await JsonAsync(response)).GetProperty("error");
        Assert.Equal(JobMessageKeys.CronInvalid, error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task 저장과실행_권한이있으면SP를부른다()
    {
        var token = _web.SignIn("job.runner", AllFuncs);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(token, "Save", Save("*/5 * * * *"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(token, "Run", new { jobId = "JOB_MAIL_SEND" })).StatusCode);

        Assert.Equal("*/5 * * * *", _web.Sp.LastCall(JobAdminService.SaveSp).Parameters["P_CRON_EXPR"]);
        Assert.Equal("JOB_MAIL_SEND", _web.Sp.LastCall(JobAdminService.RunSp).Parameters["P_JOB_ID"]);
    }

    [Fact]
    public async Task 저장_제한시간범위밖은_입력오류()
    {
        var response = await PostAsync(_web.SignIn("job.bad", AllFuncs), "Save", new { jobId = "JOB_MAIL_SEND", editVer = 1, cronExpr = "* * * * *", timeoutMin = 0, useYn = "Y" });

        Assert.Equal("MSG.INVALID_INPUT", (await JsonAsync(response)).GetProperty("error").GetProperty("code").GetString());
    }
}
