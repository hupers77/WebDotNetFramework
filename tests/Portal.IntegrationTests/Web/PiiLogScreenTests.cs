using System.Net;
using Portal.Core.Results;
using Portal.Web.Areas.Log.Models;
using Portal.Web.Areas.Log.Services;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>개인정보 접속기록 화면: 화면 선택지, 엑셀 버튼은 EXPORT만, 조건을 SP 파라미터(UTC)로 넘김, 화면 이름 채움, 엑셀은 사유·개인정보 접속기록, 권한 없는 요청 차단.</summary>
public sealed class PiiLogScreenTests : IClassFixture<PortalWebFactory>
{
    private const string Program = "LOG_PII_INQ";
    private const string Home = "DSH_HOME:READ";
    private const string Period = "fromDt=2026-10-01T15:00:00Z&toDt=2026-10-08T15:00:00Z";
    private static readonly string[] AllFuncs = [Home, $"{Program}:READ", $"{Program}:EXPORT"];
    private readonly PortalWebFactory _web;

    public PiiLogScreenTests(PortalWebFactory web)
    {
        _web = web;
        web.Sp.Returns(PiiLogService.ListSp, new PageResult<PiiLogDto>
        {
            Items = [new() { LogId = 1, LogDt = new DateTime(2026, 10, 2), UserId = "kim", ProgramId = "IT_NO_MENU", ActionCd = "DOWNLOAD", TargetDesc = """{"cnt":3}""", Reason = "점검", ClientIp = "10.0.0.1" }],
            TotalCount = 1,
            PageNo = 1,
            PageSize = PiiLogService.PageSize,
        });
        web.Sp.Returns("LOG.USP_PII_ACCESS_LOG_WRITE", new Portal.Data.SaveResult(1, null));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> PageAsync(string token)
    {
        var response = await _web.NewClient(token).GetAsync("/Log/Pii?_layout=tab", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private async Task<HttpResponseMessage> PostAsync(string token, object body)
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(token), "/");
        var request = Ajax(HttpMethod.Post, "/Log/Pii/Export", body);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await _web.NewClient(token, cookie).SendAsync(request, Ct);
    }

    [Fact]
    public async Task 화면_작업선택지와_엑셀버튼은내보내기권한만()
    {
        var all = await PageAsync(_web.SignIn("pl.admin", AllFuncs));
        var reader = await PageAsync(_web.SignIn("pl.reader", [Home, $"{Program}:READ"]));

        Assert.Contains("<option value=\"UNMASK\">", all, StringComparison.Ordinal);
        Assert.Contains("id=\"pii-export\"", all, StringComparison.Ordinal);
        Assert.Contains("js/log/pii.js", all, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"pii-export\"", reader, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 목록_조건을UTC로넘기고_화면이름을채운다()
    {
        var token = _web.SignIn("pl.list", AllFuncs);

        var json = await JsonAsync(await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, $"/Log/Pii/List?{Period}&userId=kim&programId=SYS_USER_MGT&actionCd=DOWNLOAD&ip=10.0.0.1"), Ct));

        var row = json.GetProperty("data").GetProperty("items")[0];
        Assert.Equal(("IT_NO_MENU", "점검"), (row.GetProperty("programNm").GetString(), row.GetProperty("reason").GetString()));
        var p = _web.Sp.LastCall(PiiLogService.ListSp).Parameters;
        Assert.Equal(new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Utc), (DateTime)p["P_FROM_DT"]!);
        Assert.Equal(("kim", "SYS_USER_MGT", "DOWNLOAD", "10.0.0.1"), ((string?)p["P_TARGET_USER_ID"], (string?)p["P_PROGRAM_ID"], (string?)p["P_ACTION_CD"], (string?)p["P_IP"]));
    }

    [Fact]
    public async Task 작업코드가잘못되거나_기간이없으면_거부()
    {
        var token = _web.SignIn("pl.bad", AllFuncs);

        var bad = await JsonAsync(await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, $"/Log/Pii/List?{Period}&actionCd=EDIT"), Ct));
        var noPeriod = await JsonAsync(await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Log/Pii/List"), Ct));

        Assert.False(bad.GetProperty("success").GetBoolean());
        Assert.Equal(LogMessageKeys.Period, noPeriod.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task 엑셀_사유를받아개인정보접속기록_권한이없으면403()
    {
        var search = new { fromDt = "2026-10-01T15:00:00Z", toDt = "2026-10-08T15:00:00Z", userId = "kim", actionCd = "DOWNLOAD" };

        var ok = await PostAsync(_web.SignIn("pl.export", AllFuncs), new { search, reason = "보안 점검" });

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ok.Content.Headers.ContentType?.MediaType);
        var pii = _web.Sp.LastCall("LOG.USP_PII_ACCESS_LOG_WRITE").Parameters;
        Assert.Equal((Program, "DOWNLOAD", "보안 점검"), ((string?)pii["P_PROGRAM_ID"], (string?)pii["P_ACTION_CD"], (string?)pii["P_REASON"]));
        Assert.DoesNotContain("kim", (string?)pii["P_TARGET_DESC"] ?? "", StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(_web.SignIn("pl.reader2", [Home, $"{Program}:READ"]), new { search, reason = "보안 점검" })).StatusCode);
    }
}
