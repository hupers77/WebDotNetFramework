using System.Net;
using Portal.Data;
using Portal.Web.Areas.Rpt.Models;
using Portal.Web.Areas.Rpt.Services;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>
/// 화면 [인쇄] 버튼과 현재 화면 PDF(<c>/Rpt/Print/Pdf</c>): 버튼은 PRINT 권한일 때만(프로그램·인쇄 영역 표시),
/// 받은 글자는 인코딩해 그림, 열이 많으면 가로, 개인정보 화면은 사유·접속기록, 권한·프로그램·요청 모양 검사, 접근 기록에 화면 프로그램.
/// </summary>
public sealed class ScreenPrintTests : IClassFixture<PortalWebFactory>
{
    private const string Screen = "LOG_PII_INQ";
    private const string PiiScreen = "IT_PII_SCREEN";
    private const string Home = "DSH_HOME:READ";
    private static readonly string[] AllFuncs = [Home, $"{Screen}:READ", $"{Screen}:PRINT", $"{PiiScreen}:PRINT"];
    private readonly PortalWebFactory _web;

    public ScreenPrintTests(PortalWebFactory web)
    {
        _web = web;
        web.Sp.Returns(ScreenPrintService.ProgramSp, p => (string?)p["P_PROGRAM_ID"] switch
        {
            Screen => new ProgramInfo(Screen, "LOG", "N"),
            PiiScreen => new ProgramInfo(PiiScreen, "SYS", "Y"),
            _ => null,
        });
        web.Sp.Returns("LOG.USP_PII_ACCESS_LOG_GET_LIST", new Portal.Core.Results.PageResult<Portal.Web.Areas.Log.Models.PiiLogDto> { Items = [], TotalCount = 0, PageNo = 1, PageSize = 50 });
        web.Sp.Returns("LOG.USP_PII_ACCESS_LOG_WRITE", new SaveResult(1, null));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object Body(string programId, int columns = 2, string? reason = null, string cell = "<script>x</script>") => new
    {
        programId,
        conditions = new[] { new { label = "기간", value = "2026-10-01 ~ 2026-10-07" }, new { label = "IP", value = "" } },
        tables = new[]
        {
            new
            {
                caption = (string?)null,
                columns = Enumerable.Range(1, columns).Select(i => $"열{i}").ToArray(),
                rows = new[] { Enumerable.Range(1, columns).Select(i => i == 1 ? cell : "1,234").ToArray() },
            },
        },
        reason,
    };

    private async Task<HttpResponseMessage> PostAsync(string token, object body)
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(token), "/");
        var request = Ajax(HttpMethod.Post, "/Rpt/Print/Pdf", body);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await _web.NewClient(token, cookie).SendAsync(request, Ct);
    }

    [Fact]
    public async Task 인쇄버튼은PRINT권한일때만_프로그램과인쇄영역표시()
    {
        var all = await _web.NewClient(_web.SignIn("sp.all", AllFuncs)).GetStringAsync("/Log/Pii?_layout=tab", Ct);
        var reader = await _web.NewClient(_web.SignIn("sp.reader", [Home, $"{Screen}:READ"])).GetStringAsync("/Log/Pii?_layout=tab", Ct);

        Assert.Contains("data-print-area=\"#pii-screen\"", all, StringComparison.Ordinal);
        Assert.Contains("data-func=\"PRINT\" data-program=\"LOG_PII_INQ\"", all, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"pii-print\"", reader, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PDF_글자는인코딩_빈조건생략_세로_접근기록에화면프로그램()
    {
        var token = _web.SignIn("sp.pdf", AllFuncs);
        _web.DrainAccessLog();

        var response = await PostAsync(token, Body(Screen));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var html = _web.Pdf.LastHtml!;
        Assert.Contains("&lt;script&gt;x&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("<dd>2026-10-01 ~ 2026-10-07</dd>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<dt>IP</dt>", html, StringComparison.Ordinal);
        Assert.Contains("<td class=\"num\">1,234</td>", html, StringComparison.Ordinal);
        Assert.Contains("<style>", html, StringComparison.Ordinal);
        Assert.False(_web.Pdf.LastOptions!.Landscape);
        var log = Assert.Single(_web.DrainAccessLog(), e => e.RequestPath == "/Rpt/Print/Pdf");
        Assert.Equal((Screen, "PRINT"), (log.ProgramId, log.FuncCd));
    }

    [Fact]
    public async Task 열이많으면가로()
    {
        await PostAsync(_web.SignIn("sp.wide", AllFuncs), Body(Screen, columns: 8));

        Assert.True(_web.Pdf.LastOptions!.Landscape);
    }

    [Fact]
    public async Task PRINT권한없으면403_없는프로그램_모양이틀리면업무오류()
    {
        var reader = await PostAsync(_web.SignIn("sp.no", [Home, $"{Screen}:READ"]), Body(Screen));
        var token = _web.SignIn("sp.bad", AllFuncs);
        var unknown = await JsonAsync(await PostAsync(token, Body("IT_NONE")));
        var bad = await JsonAsync(await PostAsync(token, new { programId = Screen, tables = new[] { new { columns = new[] { "A", "B" }, rows = new[] { new[] { "1" } } } } }));
        var none = await JsonAsync(await PostAsync(token, new { programId = Screen, tables = Array.Empty<object>() }));

        Assert.Equal(HttpStatusCode.Forbidden, reader.StatusCode);
        Assert.Equal("MSG.NOT_FOUND", unknown.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("MSG.INVALID_INPUT", bad.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("MSG.INVALID_INPUT", none.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task 개인정보화면은사유필수_사유와함께다운로드기록()
    {
        var token = _web.SignIn("sp.pii", AllFuncs);

        var noReason = await JsonAsync(await PostAsync(token, Body(PiiScreen)));
        var ok = await PostAsync(token, Body(PiiScreen, reason: "점검 보고"));

        Assert.Equal("MSG.PII_REASON_REQUIRED", noReason.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var pii = _web.Sp.LastCall("LOG.USP_PII_ACCESS_LOG_WRITE").Parameters;
        Assert.Equal((PiiScreen, "DOWNLOAD", "점검 보고"), ((string?)pii["P_PROGRAM_ID"], (string?)pii["P_ACTION_CD"], (string?)pii["P_REASON"]));
        Assert.Equal("""{"cnt":1,"format":"SCREEN_PDF"}""", (string?)pii["P_TARGET_DESC"]);
    }
}
