using System.Net;
using Portal.Data;
using Portal.Web.Areas.Rpt.Models;
using Portal.Web.Areas.Rpt.Services;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>
/// 레포트 화면: 버튼은 레포트 프로그램의 PRINT·EXPORT 권한별, 조건은 정의에 있는 것만 SP로 넘김, 필수·형식 검사, 최대 행 수,
/// 미리보기 HTML(머리글·결재란·합계), PDF는 CSS를 넣은 같은 HTML·가로·출력자 바닥글, 엑셀, 개인정보 레포트는 사유·접속기록,
/// 권한 없는 요청 차단, 접근 기록에 레포트 프로그램이 남는다.
/// </summary>
public sealed class ReportViewerScreenTests : IClassFixture<PortalWebFactory>
{
    private const string Report = "RPT_TEST";
    private const string PiiReport = "RPT_TEST_PII";
    private const string DataSp = "LOG.USP_RPT_TEST_GET_LIST";
    private const string Home = "DSH_HOME:READ";
    private const string Params = """[{"name":"P_MONTH","type":"MONTH","required":true,"labelKey":"LBL.MONTH","default":"PREV_MONTH"},{"name":"P_KEYWORD","type":"TEXT","labelKey":"LBL.KEYWORD","maxLength":5}]""";
    private static readonly string[] AllFuncs = [Home, $"{Report}:READ", $"{Report}:PRINT", $"{Report}:EXPORT", $"{PiiReport}:READ", $"{PiiReport}:EXPORT"];
    private readonly PortalWebFactory _web;

    public ReportViewerScreenTests(PortalWebFactory web)
    {
        _web = web;
        web.Sp.Returns(ReportService.GetSp, p => (string?)p["P_REPORT_ID"] switch
        {
            Report => new ReportDefinition(Report, DataSp, null, Params, "L", "Y", "Y", 3, "N"),
            PiiReport => new ReportDefinition(PiiReport, DataSp, null, Params, "P", "N", "N", 3, "Y"),
            _ => null,
        });
        web.Sp.Returns(DataSp, p => (string?)p["P_KEYWORD"] == "many"
            ? Enumerable.Range(1, 4).Select(i => Row(i, i)).ToList()
            : new List<SpRow> { Row(1, 2), Row(2, 5) });
        web.Sp.Returns("LOG.USP_PII_ACCESS_LOG_WRITE", new SaveResult(1, null));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SpRow Row(int day, int count) =>
        new(["LOG_DATE", "OK_CNT", "NOTE"], [new DateOnly(2026, 9, day), count, day == 1 ? "<b>첫날</b>" : null]);

    private async Task<HttpResponseMessage> PageAsync(string token, string id = Report) =>
        await _web.NewClient(token).GetAsync($"/Rpt/Viewer/Index/{id}?_layout=tab", Ct);

    private async Task<System.Text.Json.JsonElement> PreviewAsync(string token, string query, string id = Report) =>
        await JsonAsync(await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, $"/Rpt/Viewer/Preview/{id}?{query}"), Ct));

    private async Task<HttpResponseMessage> PostAsync(string token, string url, object body)
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(token), "/");
        var request = Ajax(HttpMethod.Post, url, body);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await _web.NewClient(token, cookie).SendAsync(request, Ct);
    }

    [Fact]
    public async Task 화면_조건기본값_버튼은PRINT_EXPORT권한별_없는레포트404_권한없으면403()
    {
        var all = await (await PageAsync(_web.SignIn("rv.all", AllFuncs))).Content.ReadAsStringAsync(Ct);
        var reader = await (await PageAsync(_web.SignIn("rv.reader", [Home, $"{Report}:READ"]))).Content.ReadAsStringAsync(Ct);

        var prev = DateTime.Now.AddMonths(-1).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains($"name=\"P_MONTH\" type=\"month\" value=\"{prev}\" required=\"required\"", all, StringComparison.Ordinal);
        Assert.Contains("maxlength=\"5\"", all, StringComparison.Ordinal);
        Assert.Contains("id=\"rpt-pdf\"", all, StringComparison.Ordinal);
        Assert.Contains("id=\"rpt-excel\"", all, StringComparison.Ordinal);
        Assert.Contains("sandbox=\"allow-same-origin\"", all, StringComparison.Ordinal);
        Assert.Contains("js/rpt/viewer.js", all, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"rpt-pdf\"", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"rpt-excel\"", reader, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await PageAsync(_web.SignIn("rv.none1", AllFuncs), "RPT_NONE")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PageAsync(_web.SignIn("rv.none2", [Home]))).StatusCode);
    }

    [Fact]
    public async Task 미리보기_정의된조건만SP로_머리글결재란합계_값은인코딩_접근기록에레포트프로그램()
    {
        var token = _web.SignIn("rv.preview", AllFuncs);
        _web.DrainAccessLog();

        var json = await PreviewAsync(token, "P_MONTH=2026-09&P_KEYWORD=%20ab%20&P_OTHER=x&P_MAX_ROWS=999");

        Assert.True(json.GetProperty("success").GetBoolean(), json.ToString());
        var html = json.GetProperty("data").GetProperty("html").GetString()!;
        var p = _web.Sp.LastCall(DataSp).Parameters;
        Assert.Equal(("2026-09", "ab", 3), ((string?)p["P_MONTH"], (string?)p["P_KEYWORD"], (int?)p["P_MAX_ROWS"]));
        Assert.False(p.ContainsKey("P_OTHER"));
        Assert.Contains("<link rel=\"stylesheet\" href=\"/css/report.css?v=", html, StringComparison.Ordinal);
        Assert.Contains("class=\"rpt-approval\"", html, StringComparison.Ordinal);
        Assert.Contains("<dd>2026-09</dd>", html, StringComparison.Ordinal);
        Assert.Contains("<td>2026-09-01</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td class=\"num\">7</td>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>첫날", html, StringComparison.Ordinal);
        Assert.Contains("rpt-landscape", html, StringComparison.Ordinal);
        Assert.Equal(2, json.GetProperty("data").GetProperty("rowCount").GetInt32());

        var log = Assert.Single(_web.DrainAccessLog(), e => e.RequestPath == $"/Rpt/Viewer/Preview/{Report}");
        Assert.Equal((Report, "READ"), (log.ProgramId, log.FuncCd));
    }

    [Fact]
    public async Task 미리보기_필수조건없음_형식오류_길이초과_최대행수초과는업무오류()
    {
        var token = _web.SignIn("rv.bad", AllFuncs);

        var missing = await PreviewAsync(token, "P_KEYWORD=a");
        var badMonth = await PreviewAsync(token, "P_MONTH=2026-13");
        var tooLong = await PreviewAsync(token, "P_MONTH=2026-09&P_KEYWORD=abcdef");
        var many = await PreviewAsync(token, "P_MONTH=2026-09&P_KEYWORD=many");

        Assert.Equal("VAL.REQUIRED", missing.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(ReportMessageKeys.ParamInvalid, badMonth.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(ReportMessageKeys.ParamInvalid, tooLong.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(ReportMessageKeys.TooMany, many.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task PDF_CSS를넣은HTML_가로_바닥글_PRINT권한이없으면403()
    {
        var body = new { @params = new Dictionary<string, string> { ["P_MONTH"] = "2026-09" } };

        var ok = await PostAsync(_web.SignIn("rv.pdf", AllFuncs), $"/Rpt/Viewer/Pdf/{Report}", body);

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("application/pdf", ok.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith(Report + "-", ok.Content.Headers.ContentDisposition?.FileName?.Trim('"'), StringComparison.Ordinal);
        Assert.Contains("<style>", _web.Pdf.LastHtml, StringComparison.Ordinal);
        Assert.Contains(".rpt-table", _web.Pdf.LastHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<link rel=\"stylesheet\"", _web.Pdf.LastHtml, StringComparison.Ordinal);
        Assert.True(_web.Pdf.LastOptions!.Landscape);
        Assert.NotNull(_web.Pdf.LastOptions.FooterText);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(_web.SignIn("rv.pdf.no", [Home, $"{Report}:READ", $"{Report}:EXPORT"]), $"/Rpt/Viewer/Pdf/{Report}", body)).StatusCode);
    }

    [Fact]
    public async Task 엑셀_EXPORT권한_개인정보레포트는사유필수_접속기록()
    {
        var body = new { @params = new Dictionary<string, string> { ["P_MONTH"] = "2026-09" } };
        var token = _web.SignIn("rv.excel", AllFuncs);

        var ok = await PostAsync(token, $"/Rpt/Viewer/Excel/{Report}", body);
        var noReason = await JsonAsync(await PostAsync(token, $"/Rpt/Viewer/Excel/{PiiReport}", body));
        var withReason = await PostAsync(token, $"/Rpt/Viewer/Excel/{PiiReport}", new { body.@params, reason = "월간 점검" });

        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ok.Content.Headers.ContentType?.MediaType);
        Assert.Equal("MSG.PII_REASON_REQUIRED", noReason.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, withReason.StatusCode);
        var pii = _web.Sp.LastCall("LOG.USP_PII_ACCESS_LOG_WRITE").Parameters;
        Assert.Equal((PiiReport, "DOWNLOAD", "월간 점검"), ((string?)pii["P_PROGRAM_ID"], (string?)pii["P_ACTION_CD"], (string?)pii["P_REASON"]));
        Assert.DoesNotContain("2026-09", (string?)pii["P_TARGET_DESC"] ?? "", StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(_web.SignIn("rv.excel.no", [Home, $"{Report}:READ"]), $"/Rpt/Viewer/Excel/{Report}", body)).StatusCode);
    }
}
