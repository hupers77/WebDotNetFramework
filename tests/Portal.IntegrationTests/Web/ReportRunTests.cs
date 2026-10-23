using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Portal.Core.Errors;
using Portal.Data;
using Portal.Framework.Web.Context;
using Portal.Web.Areas.Rpt.Models;
using Portal.Web.Areas.Rpt.Services;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>
/// 레포트 백그라운드 생성: 요청(형식별 PRINT·EXPORT 권한, 조건 검사, 개인정보 사유), 바로 만드는 행 수 한도, 내 레포트 화면(목록·내려받기·삭제),
/// 대기열 처리(요청자 권한·데이터 범위로 실행, 파일 저장, 끝내기, 실패 사유, 권한 상실, 서버 멈춤), 만료 정리.
/// </summary>
public sealed class ReportRunTests : IClassFixture<PortalWebFactory>
{
    private const string Report = "RPT_BG";
    private const string PiiReport = "RPT_BG_PII";
    private const string DataSp = "LOG.USP_RPT_BG_GET_LIST";
    private const string Home = "DSH_HOME:READ";
    private const string Runs = "RPT_RUNS";
    private const string Params = """[{"name":"P_MONTH","type":"MONTH","required":true,"labelKey":"LBL.MONTH"},{"name":"P_KEYWORD","type":"TEXT","labelKey":"LBL.KEYWORD","maxLength":5}]""";
    private static readonly string[] AllFuncs = [Home, $"{Report}:READ", $"{Report}:PRINT", $"{Report}:EXPORT", $"{PiiReport}:READ", $"{PiiReport}:EXPORT", $"{Runs}:READ", $"{Runs}:DELETE"];
    private readonly PortalWebFactory _web;
    private readonly Queue<ClaimedReportRun> _claims = new();
    private string? _dataUser;
    private string? _piiUser;

    public ReportRunTests(PortalWebFactory web)
    {
        _web = web;
        web.Sp.Returns(ReportService.GetSp, p => (string?)p["P_REPORT_ID"] switch
        {
            Report => new ReportDefinition(Report, DataSp, null, Params, "P", "N", "Y", 8000, "N"),
            PiiReport => new ReportDefinition(PiiReport, DataSp, null, Params, "P", "N", "N", 8000, "Y"),
            _ => null,
        });
        web.Sp.Returns(DataSp, p =>
        {
            _dataUser = ActingUser();
            return Enumerable.Range(1, (string?)p["P_KEYWORD"] == "many" ? 9000 : 3).Select(i => Row(i, i)).ToList();
        });
        web.Sp.Returns("LOG.USP_PII_ACCESS_LOG_WRITE", _ =>
        {
            _piiUser = ActingUser();
            return new SaveResult(1, null);
        });
        web.Sp.Returns(ReportRunService.SaveSp, new SaveResult(1, "77"));
        web.Sp.Returns(ReportRunProcessor.ClaimSp, _ =>
        {
            lock (_claims)
            {
                return _claims.Count > 0 ? _claims.Dequeue() : null;
            }
        });
        web.Sp.Returns(ReportRunProcessor.EndSp, new SaveResult(1, null));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SpRow Row(int day, int count) => new(["LOG_DATE", "OK_CNT"], [new DateOnly(2026, 9, 1).AddDays(day), count]);

    private static ClaimedReportRun Claimed(string user, string report = Report, string format = "PDF", string paramsJson = """{"P_MONTH":"2026-09"}""", string? reason = null) => new()
    {
        RunId = 5,
        ReportId = report,
        FormatCd = format,
        ParamsJson = paramsJson,
        Reason = reason,
        ReqUserId = user,
        ReqCompanyCd = "HQ",
        ReqUserTypeCd = "EMP",
        ReqLangCd = "ko",
    };

    private void Enqueue(ClaimedReportRun run)
    {
        lock (_claims)
        {
            _claims.Enqueue(run);
        }
    }

    private async Task<HttpResponseMessage> PostAsync(string token, string url, object body)
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(token), "/");
        var request = Ajax(HttpMethod.Post, url, body);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await _web.NewClient(token, cookie).SendAsync(request, Ct);
    }

    private Task<HttpResponseMessage> QueueAsync(string token, string format, Dictionary<string, string>? @params = null, string id = Report, string? reason = null) =>
        PostAsync(token, $"/Rpt/Viewer/Queue/{id}", new { format, @params = @params ?? new Dictionary<string, string> { ["P_MONTH"] = "2026-09" }, reason });

    /// <summary>지금 실행 중인 흐름의 사용자 (백그라운드 처리가 요청자로 동작하는지 SP 안에서 확인한다).</summary>
    private string? ActingUser() => _web.Services.GetRequiredService<IHttpContextAccessor>().HttpContext?.Features.Get<PortalUserFeature>()?.User.UserId;

    [Fact]
    public async Task 화면_백그라운드버튼은_형식별권한으로만()
    {
        var all = await (await _web.NewClient(_web.SignIn("rb.all", AllFuncs)).GetAsync($"/Rpt/Viewer/Index/{Report}?_layout=tab", Ct)).Content.ReadAsStringAsync(Ct);
        var pdfOnly = await (await _web.NewClient(_web.SignIn("rb.pdf", [Home, $"{Report}:READ", $"{Report}:PRINT"])).GetAsync($"/Rpt/Viewer/Index/{Report}?_layout=tab", Ct)).Content.ReadAsStringAsync(Ct);

        Assert.Contains("id=\"rpt-pdf-bg\"", all, StringComparison.Ordinal);
        Assert.Contains("id=\"rpt-excel-bg\"", all, StringComparison.Ordinal);
        Assert.Contains("data-url-queue=\"/Rpt/Viewer/Queue/RPT_BG\"", all, StringComparison.Ordinal);
        Assert.Contains("id=\"rpt-pdf-bg\"", pdfOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"rpt-excel-bg\"", pdfOnly, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 요청_정의된조건만_공백제거해_대기열에_번호반환()
    {
        var token = _web.SignIn("rb.queue", AllFuncs);

        var pdf = await JsonAsync(await QueueAsync(token, "pdf", new Dictionary<string, string> { ["P_MONTH"] = " 2026-09 ", ["P_KEYWORD"] = " ab ", ["P_OTHER"] = "x" }));
        var excel = await JsonAsync(await QueueAsync(token, "EXCEL"));

        Assert.True(pdf.GetProperty("success").GetBoolean(), pdf.ToString());
        Assert.Equal(77, pdf.GetProperty("data").GetProperty("runId").GetInt64());
        Assert.True(excel.GetProperty("success").GetBoolean());
        var saves = _web.Sp.Calls.Where(c => c.SpName == ReportRunService.SaveSp).ToList();
        Assert.Equal(("RPT_BG", "PDF", """{"P_MONTH":"2026-09","P_KEYWORD":"ab"}"""), ((string?)saves[0].Parameters["P_REPORT_ID"], (string?)saves[0].Parameters["P_FORMAT_CD"], (string?)saves[0].Parameters["P_PARAMS_JSON"]));
        Assert.Equal("EXCEL", (string?)saves[1].Parameters["P_FORMAT_CD"]);
    }

    [Fact]
    public async Task 요청_형식에맞는권한이없으면403_형식오류_조건오류는저장안함()
    {
        var before = _web.Sp.Calls.Count(c => c.SpName == ReportRunService.SaveSp);
        var pdfOnly = _web.SignIn("rb.noexcel", [Home, $"{Report}:READ", $"{Report}:PRINT"]);
        var token = _web.SignIn("rb.bad", AllFuncs);

        var noExcel = await QueueAsync(pdfOnly, "EXCEL");
        var badFormat = await QueueAsync(token, "CSV");
        var badMonth = await JsonAsync(await QueueAsync(token, "PDF", new Dictionary<string, string> { ["P_MONTH"] = "2026-13" }));
        var missing = await JsonAsync(await QueueAsync(token, "PDF", new Dictionary<string, string> { ["P_KEYWORD"] = "a" }));
        var unknown = await QueueAsync(token, "PDF", id: "RPT_NONE");

        Assert.Equal(HttpStatusCode.Forbidden, noExcel.StatusCode);
        Assert.False((await JsonAsync(badFormat)).GetProperty("success").GetBoolean());
        Assert.Equal(ReportMessageKeys.ParamInvalid, badMonth.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("VAL.REQUIRED", missing.GetProperty("error").GetProperty("code").GetString());
        Assert.False((await JsonAsync(unknown)).GetProperty("success").GetBoolean());
        Assert.Equal(before, _web.Sp.Calls.Count(c => c.SpName == ReportRunService.SaveSp));
    }

    [Fact]
    public async Task 요청_개인정보레포트는_사유필수_사유는요청에보관()
    {
        var token = _web.SignIn("rb.pii", AllFuncs);

        var noReason = await JsonAsync(await QueueAsync(token, "EXCEL", id: PiiReport));
        var ok = await JsonAsync(await QueueAsync(token, "EXCEL", id: PiiReport, reason: "월간 점검"));

        Assert.Equal("MSG.PII_REASON_REQUIRED", noReason.GetProperty("error").GetProperty("code").GetString());
        Assert.True(ok.GetProperty("success").GetBoolean());
        Assert.Equal("월간 점검", (string?)_web.Sp.LastCall(ReportRunService.SaveSp).Parameters["P_REASON"]);
    }

    [Fact]
    public async Task 요청_진행중요청이_너무많으면_SP오류가_업무오류로()
    {
        var token = _web.SignIn("rb.limit", AllFuncs);
        _web.Sp.Throws(ReportRunService.SaveSp, new BizException(ReportMessageKeys.RunLimit));
        try
        {
            var json = await JsonAsync(await QueueAsync(token, "PDF"));

            Assert.Equal(ReportMessageKeys.RunLimit, json.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            _web.Sp.Returns(ReportRunService.SaveSp, new SaveResult(1, "77"));
        }
    }

    [Fact]
    public async Task 바로만들때는_최대5000행_넘으면_백그라운드안내()
    {
        var token = _web.SignIn("rb.sync", AllFuncs);

        var many = await JsonAsync(await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, $"/Rpt/Viewer/Preview/{Report}?P_MONTH=2026-09&P_KEYWORD=many"), Ct));

        Assert.Equal(5000, (int?)_web.Sp.LastCall(DataSp).Parameters["P_MAX_ROWS"]);
        Assert.Equal(ReportMessageKeys.UseBackground, many.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task 내레포트_화면과_목록_레포트이름_실패사유문구()
    {
        _web.Sp.Returns(ReportRunService.ListSp, new List<ReportRunItem>
        {
            new() { RunId = 1, ReportId = Report, FormatCd = "PDF", StatusCd = "FAILED", ErrKey = ReportMessageKeys.TooMany, ErrArg = "8000", RegDt = DateTime.UtcNow },
            new() { RunId = 2, ReportId = Report, FormatCd = "EXCEL", StatusCd = "DONE", RowCnt = 3, FileNm = "RPT_BG-1.xlsx", FileSize = 10, RegDt = DateTime.UtcNow },
        });
        var token = _web.SignIn("rb.list", AllFuncs);
        _web.DrainAccessLog();

        var page = await _web.NewClient(token).GetAsync("/Rpt/Runs?_layout=tab", Ct);
        var list = await JsonAsync(await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Rpt/Runs/List"), Ct));
        var forbidden = await _web.NewClient(_web.SignIn("rb.list.no", [Home])).GetAsync("/Rpt/Runs?_layout=tab", Ct);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("js/rpt/runs.js", await page.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var items = list.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal("FAILED", items[0].GetProperty("statusCd").GetString());
        Assert.Contains("RPT.TOO_MANY", items[0].GetProperty("errMsg").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(_web.DrainAccessLog(), e => e.RequestPath == "/Rpt/Runs/List");
    }

    [Fact]
    public async Task 내려받기_완료된파일만_그레포트의형식권한도_다시확인_접근기록()
    {
        _web.Files.Files["rpt/2026/10/abc"] = [1, 2, 3];
        _web.Sp.Returns(ReportRunService.GetSp, p => (long?)p["P_RUN_ID"] switch
        {
            2 => new ReportRunFile { RunId = 2, ReportId = Report, FormatCd = "EXCEL", StatusCd = "DONE", FileNm = "RPT_BG-1.xlsx", FileSize = 3, StoredPath = "rpt/2026/10/abc" },
            3 => new ReportRunFile { RunId = 3, ReportId = Report, FormatCd = "PDF", StatusCd = "RUNNING" },
            _ => null,
        });
        var token = _web.SignIn("rb.down", AllFuncs);
        var noExport = _web.SignIn("rb.down.no", [Home, $"{Runs}:READ", $"{Report}:PRINT"]);
        _web.DrainAccessLog();

        var ok = await PostAsync(token, "/Rpt/Runs/Download", new { runId = 2 });
        var forbidden = await PostAsync(noExport, "/Rpt/Runs/Download", new { runId = 2 });
        var running = await JsonAsync(await PostAsync(token, "/Rpt/Runs/Download", new { runId = 3 }));
        var others = await JsonAsync(await PostAsync(token, "/Rpt/Runs/Download", new { runId = 99 }));

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal([1, 2, 3], await ok.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal("RPT_BG-1.xlsx", ok.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("MSG.NOT_FOUND", running.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("MSG.NOT_FOUND", others.GetProperty("error").GetProperty("code").GetString());
        var log = _web.DrainAccessLog().First(e => e.RequestPath == "/Rpt/Runs/Download");
        Assert.Equal((Runs, "EXPORT"), (log.ProgramId, log.FuncCd));
    }

    [Fact]
    public async Task 삭제_DELETE권한필요_없는요청은_업무오류()
    {
        var reader = _web.SignIn("rb.del.no", [Home, $"{Runs}:READ"]);
        var token = _web.SignIn("rb.del", AllFuncs);
        _web.Sp.Returns(ReportRunService.DeleteSp, p => new SaveResult((long?)p["P_RUN_ID"] == 2 ? 1 : 0, null));

        var forbidden = await PostAsync(reader, "/Rpt/Runs/Delete", new { runId = 2 });
        var ok = await JsonAsync(await PostAsync(token, "/Rpt/Runs/Delete", new { runId = 2 }));
        var none = await JsonAsync(await PostAsync(token, "/Rpt/Runs/Delete", new { runId = 9 }));

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.True(ok.GetProperty("success").GetBoolean());
        Assert.Equal("MSG.NOT_FOUND", none.GetProperty("error").GetProperty("code").GetString());
    }

    private ReportRunProcessor Processor() => _web.Services.GetRequiredService<ReportRunProcessor>();

    [Fact]
    public async Task 처리_대기열이_비어있으면_false()
    {
        Assert.False(await Processor().ProcessNextAsync(Ct));
    }

    [Fact]
    public async Task 처리_요청자로_실행해_파일을저장하고_완료로끝낸다_엑셀()
    {
        _web.SignIn("rb.proc.excel", AllFuncs, langCd: "ko");
        Enqueue(Claimed("rb.proc.excel", format: "EXCEL"));

        var worked = await Processor().ProcessNextAsync(Ct);

        Assert.True(worked);
        var data = _web.Sp.LastCall(DataSp).Parameters;
        Assert.Equal(("rb.proc.excel", "2026-09", 8000), (_dataUser, (string?)data["P_MONTH"], (int?)data["P_MAX_ROWS"]));
        var end = _web.Sp.LastCall(ReportRunProcessor.EndSp).Parameters;
        Assert.Equal(("DONE", 3), ((string?)end["P_STATUS_CD"], (int?)end["P_ROW_CNT"]));
        var path = (string)end["P_STORED_PATH"]!;
        Assert.StartsWith("rpt/", path, StringComparison.Ordinal);
        Assert.Equal((long)_web.Files.Files[path].Length, (long)end["P_FILE_SIZE"]!);
        Assert.StartsWith("RPT_BG-", (string)end["P_FILE_NM"]!, StringComparison.Ordinal);
        Assert.EndsWith(".xlsx", (string)end["P_FILE_NM"]!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 처리_PDF는_템플릿을그려_변환하고_개인정보레포트는_사유와함께_다운로드기록()
    {
        _web.SignIn("rb.proc.pdf", AllFuncs);
        Enqueue(Claimed("rb.proc.pdf", PiiReport, "EXCEL", reason: "월간 점검"));
        Enqueue(Claimed("rb.proc.pdf"));

        await Processor().ProcessNextAsync(Ct);
        var pii = _web.Sp.LastCall("LOG.USP_PII_ACCESS_LOG_WRITE").Parameters;
        await Processor().ProcessNextAsync(Ct);

        Assert.Equal((PiiReport, "DOWNLOAD", "월간 점검", "rb.proc.pdf"), ((string?)pii["P_PROGRAM_ID"], (string?)pii["P_ACTION_CD"], (string?)pii["P_REASON"], _piiUser));
        Assert.Contains("class=\"rpt-table\"", _web.Pdf.LastHtml, StringComparison.Ordinal);
        Assert.Contains("<style>", _web.Pdf.LastHtml, StringComparison.Ordinal);
        Assert.EndsWith(".pdf", (string)_web.Sp.LastCall(ReportRunProcessor.EndSp).Parameters["P_FILE_NM"]!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 처리_행이_최대를넘으면_실패로끝내고_파일은없다()
    {
        _web.SignIn("rb.proc.many", AllFuncs);
        var files = _web.Files.Files.Count;
        Enqueue(Claimed("rb.proc.many", paramsJson: """{"P_MONTH":"2026-09","P_KEYWORD":"many"}"""));

        await Processor().ProcessNextAsync(Ct);

        var end = _web.Sp.LastCall(ReportRunProcessor.EndSp).Parameters;
        Assert.Equal(("FAILED", ReportMessageKeys.TooMany, "8000"), ((string?)end["P_STATUS_CD"], (string?)end["P_ERR_KEY"], (string?)end["P_ERR_ARG"]));
        Assert.Equal(files, _web.Files.Files.Count);
    }

    [Fact]
    public async Task 처리_요청한뒤_권한이_없어졌으면_만들지않고_실패()
    {
        _web.SignIn("rb.proc.revoked", [Home, $"{Report}:READ"]);
        var calls = _web.Sp.Calls.Count(c => c.SpName == DataSp);
        Enqueue(Claimed("rb.proc.revoked"));

        await Processor().ProcessNextAsync(Ct);

        Assert.Equal("MSG.FORBIDDEN", (string?)_web.Sp.LastCall(ReportRunProcessor.EndSp).Parameters["P_ERR_KEY"]);
        Assert.Equal(calls, _web.Sp.Calls.Count(c => c.SpName == DataSp));
    }

    [Fact]
    public async Task 처리_뜻밖의오류는_시스템오류로_끝내고_다음요청은_계속()
    {
        _web.SignIn("rb.proc.boom", AllFuncs);
        Enqueue(Claimed("rb.proc.boom", report: "RPT_NONE"));
        Enqueue(Claimed("rb.proc.boom"));

        Assert.True(await Processor().ProcessNextAsync(Ct));
        Assert.Equal("MSG.NOT_FOUND", (string?)_web.Sp.LastCall(ReportRunProcessor.EndSp).Parameters["P_ERR_KEY"]);
        Assert.True(await Processor().ProcessNextAsync(Ct));
        Assert.Equal("DONE", (string?)_web.Sp.LastCall(ReportRunProcessor.EndSp).Parameters["P_STATUS_CD"]);
    }

    [Fact]
    public async Task 처리_끝내기가_0건이면_이미_지운요청_저장한파일을_지운다()
    {
        _web.SignIn("rb.proc.gone", AllFuncs);
        var before = _web.Files.Files.Keys.ToHashSet();
        _web.Sp.Returns(ReportRunProcessor.EndSp, new SaveResult(0, null));
        try
        {
            Enqueue(Claimed("rb.proc.gone", format: "EXCEL"));

            await Processor().ProcessNextAsync(Ct);

            Assert.Empty(_web.Files.Files.Keys.Except(before));
        }
        finally
        {
            _web.Sp.Returns(ReportRunProcessor.EndSp, new SaveResult(1, null));
        }
    }

    [Fact]
    public async Task 정리_지운요청은_행을_지운뒤에만_파일을_지운다()
    {
        _web.Files.Files["rpt/2026/10/gone"] = [1];
        _web.Files.Files["rpt/2026/10/kept"] = [1];
        _web.Sp.Returns(ReportRunProcessor.CleanupListSp, new List<ReportRunProcessor.CleanupTarget>
        {
            new() { RunId = 10, StoredPath = "rpt/2026/10/gone" },
            new() { RunId = 11, StoredPath = "rpt/2026/10/kept" },
            new() { RunId = 12, StoredPath = null },
        });
        _web.Sp.Returns(ReportRunProcessor.CleanupDeleteSp, p => new SaveResult((long?)p["P_RUN_ID"] == 11 ? 0 : 1, null));

        var deleted = await Processor().CleanupAsync(Ct);

        Assert.Equal(2, deleted);
        Assert.False(_web.Files.Files.ContainsKey("rpt/2026/10/gone"));
        Assert.True(_web.Files.Files.ContainsKey("rpt/2026/10/kept"));
    }
}
