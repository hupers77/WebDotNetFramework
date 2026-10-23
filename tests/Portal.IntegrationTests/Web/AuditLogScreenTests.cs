using System.Net;
using Portal.Core.Results;
using Portal.Web.Areas.Log.Models;
using Portal.Web.Areas.Log.Services;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>데이터 변경 기록 화면: 테이블 선택지, 엑셀 버튼은 EXPORT만, 목록은 변경 전후 값 없이, 한 건은 개인정보 접속기록, 엑셀은 사유, 권한 없는 요청 차단.</summary>
public sealed class AuditLogScreenTests : IClassFixture<PortalWebFactory>
{
    private const string Program = "LOG_AUDIT_INQ";
    private const string Home = "DSH_HOME:READ";
    private const string Period = "fromDt=2026-10-01T15:00:00Z&toDt=2026-10-08T15:00:00Z";
    private static readonly string[] AllFuncs = [Home, $"{Program}:READ", $"{Program}:EXPORT"];
    private readonly PortalWebFactory _web;

    public AuditLogScreenTests(PortalWebFactory web)
    {
        _web = web;
        web.Sp.Returns(AuditLogService.TableListSp, new List<AuditLogService.TableRow> { new() { TableNm = "FW.TB_USER" } });
        var row = new AuditLogDto { LogId = 7, LogDt = new DateTime(2026, 10, 2, 1, 2, 3, 456), UserId = "kim", TableNm = "FW.TB_USER", ActionCd = "U", RowKey = "lee", BeforeJson = """{"LANG_CD":"ko"}""", AfterJson = """{"LANG_CD":"en"}""" };
        web.Sp.Returns(AuditLogService.ListSp, new PageResult<AuditLogDto> { Items = [row], TotalCount = 1, PageNo = 1, PageSize = AuditLogService.PageSize });
        web.Sp.Returns(AuditLogService.GetSp, row);
        web.Sp.Returns("LOG.USP_PII_ACCESS_LOG_WRITE", new Portal.Data.SaveResult(1, null));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> PageAsync(string token)
    {
        var response = await _web.NewClient(token).GetAsync("/Log/Audit?_layout=tab", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private async Task<HttpResponseMessage> PostAsync(string token, object body)
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(token), "/");
        var request = Ajax(HttpMethod.Post, "/Log/Audit/Export", body);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await _web.NewClient(token, cookie).SendAsync(request, Ct);
    }

    [Fact]
    public async Task 화면_테이블선택지와_엑셀버튼은내보내기권한만()
    {
        var all = await PageAsync(_web.SignIn("al.admin", AllFuncs));
        var reader = await PageAsync(_web.SignIn("al.reader", [Home, $"{Program}:READ"]));

        Assert.Contains("<option value=\"FW.TB_USER\">FW.TB_USER</option>", all, StringComparison.Ordinal);
        Assert.Contains("id=\"audit-export\"", all, StringComparison.Ordinal);
        Assert.Contains("js/log/audit.js", all, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"audit-export\"", reader, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 목록_조건을UTC로넘기고_변경전후값은빼고요청()
    {
        var token = _web.SignIn("al.list", AllFuncs);

        var json = await JsonAsync(await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, $"/Log/Audit/List?{Period}&tableNm=FW.TB_USER&actionCd=U&rowKey=le&userId=kim&ip=10.0.0.1"), Ct));

        Assert.Equal("lee", json.GetProperty("data").GetProperty("items")[0].GetProperty("rowKey").GetString());
        var p = _web.Sp.LastCall(AuditLogService.ListSp).Parameters;
        Assert.Equal(new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Utc), (DateTime)p["P_FROM_DT"]!);
        Assert.Equal(("FW.TB_USER", "U", "le", "kim", "10.0.0.1", "N"), ((string?)p["P_TABLE_NM"], (string?)p["P_ACTION_CD"], (string?)p["P_ROW_KEY"], (string?)p["P_TARGET_USER_ID"], (string?)p["P_IP"], (string?)p["P_JSON_YN"]));
    }

    [Fact]
    public async Task 작업코드가잘못되면_거부()
    {
        var token = _web.SignIn("al.badaction", AllFuncs);

        var json = await JsonAsync(await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, $"/Log/Audit/List?{Period}&actionCd=X"), Ct));

        Assert.False(json.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task 한건_변경전후값과_개인정보접속기록()
    {
        var token = _web.SignIn("al.detail", AllFuncs);

        var json = await JsonAsync(await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Log/Audit/Detail?logId=7&logDt=2026-10-02T01:02:03.456Z"), Ct));

        Assert.Equal("""{"LANG_CD":"en"}""", json.GetProperty("data").GetProperty("afterJson").GetString());
        var p = _web.Sp.LastCall(AuditLogService.GetSp).Parameters;
        Assert.Equal((7L, new DateTime(2026, 10, 2, 1, 2, 3, 456, DateTimeKind.Utc)), ((long)p["P_LOG_ID"]!, (DateTime)p["P_LOG_DT"]!));
        var pii = _web.Sp.LastCall("LOG.USP_PII_ACCESS_LOG_WRITE").Parameters;
        Assert.Equal((Program, "VIEW"), ((string?)pii["P_PROGRAM_ID"], (string?)pii["P_ACTION_CD"]));
        Assert.DoesNotContain("lee", (string?)pii["P_TARGET_DESC"] ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task 엑셀_사유를받아개인정보접속기록_변경전후값포함_권한이없으면403()
    {
        var search = new { fromDt = "2026-10-01T15:00:00Z", toDt = "2026-10-08T15:00:00Z", rowKey = "lee" };

        var ok = await PostAsync(_web.SignIn("al.export", AllFuncs), new { search, reason = "보안 점검" });

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ok.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Y", (string?)_web.Sp.LastCall(AuditLogService.ListSp).Parameters["P_JSON_YN"]);
        var pii = _web.Sp.LastCall("LOG.USP_PII_ACCESS_LOG_WRITE").Parameters;
        Assert.Equal((Program, "DOWNLOAD", "보안 점검"), ((string?)pii["P_PROGRAM_ID"], (string?)pii["P_ACTION_CD"], (string?)pii["P_REASON"]));
        Assert.DoesNotContain("lee", (string?)pii["P_TARGET_DESC"] ?? "", StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(_web.SignIn("al.reader2", [Home, $"{Program}:READ"]), new { search, reason = "보안 점검" })).StatusCode);
    }
}
