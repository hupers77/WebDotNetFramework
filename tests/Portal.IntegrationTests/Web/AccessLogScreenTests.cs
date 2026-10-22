using System.Net;
using Portal.Core.Results;
using Portal.Web.Areas.Log.Models;
using Portal.Web.Areas.Log.Services;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>로그인·접근 기록 화면: 결과 선택지, 엑셀 버튼은 EXPORT만, 조건을 SP 파라미터(UTC)로 넘김, 화면 이름 채움, 엑셀은 사유·개인정보 접속기록, 권한 없는 요청 차단.</summary>
public sealed class AccessLogScreenTests : IClassFixture<PortalWebFactory>
{
    private const string Program = "LOG_ACCESS_INQ";
    private const string Home = "DSH_HOME:READ";
    private const string Period = "fromDt=2026-10-01T15:00:00Z&toDt=2026-10-08T15:00:00Z";
    private static readonly string[] AllFuncs = [Home, $"{Program}:READ", $"{Program}:EXPORT"];
    private readonly PortalWebFactory _web;

    public AccessLogScreenTests(PortalWebFactory web)
    {
        _web = web;
        web.Sp.Returns("FW.USP_CODE_GET_LIST", new PageResult<CodeDto>
        {
            Items = [new() { Code = "FAIL_PWD", CodeNm = "비밀번호 오류", UseYn = "Y" }],
            TotalCount = 1,
            PageNo = 1,
            PageSize = 500,
        });
        web.Sp.Returns("FW.USP_CODE_LANG_GET_LIST", new List<CodeAdminService.NameRow>());
        web.Sp.Returns(AccessLogService.LoginListSp, new PageResult<LoginLogDto>
        {
            Items = [new() { LogId = 1, LogDt = new DateTime(2026, 10, 2), UserId = "kim", LoginResultCd = "FAIL_PWD", LoginResultNm = "비밀번호 오류", AuthMethodCd = "FORM", ClientIp = "10.0.0.1" }],
            TotalCount = 1,
            PageNo = 1,
            PageSize = AccessLogService.PageSize,
        });
        web.Sp.Returns(AccessLogService.AccessListSp, new PageResult<AccessLogDto>
        {
            Items = [new() { LogId = 2, LogDt = new DateTime(2026, 10, 2), UserId = "kim", ProgramId = "IT_NO_MENU", HttpMethod = "GET", RequestPath = "/x", StatusCode = 403 }],
            TotalCount = 1,
            PageNo = 1,
            PageSize = AccessLogService.PageSize,
        });
        web.Sp.Returns("LOG.USP_PII_ACCESS_LOG_WRITE", new Portal.Data.SaveResult(1, null));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> PageAsync(string token)
    {
        var response = await _web.NewClient(token).GetAsync("/Log/Access?_layout=tab", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private async Task<HttpResponseMessage> PostAsync(string token, object body)
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(token), "/");
        var request = Ajax(HttpMethod.Post, "/Log/Access/Export", body);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await _web.NewClient(token, cookie).SendAsync(request, Ct);
    }

    [Fact]
    public async Task 화면_결과선택지와_엑셀버튼은내보내기권한만()
    {
        var all = await PageAsync(_web.SignIn("la.admin", AllFuncs));
        var reader = await PageAsync(_web.SignIn("la.reader", [Home, $"{Program}:READ"]));

        Assert.Contains("<option value=\"FAIL_PWD\">비밀번호 오류</option>", all, StringComparison.Ordinal);
        Assert.Contains("id=\"log-export\"", all, StringComparison.Ordinal);
        Assert.Contains("js/log/access.js", all, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"log-export\"", reader, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 로그인기록_조건을UTC로넘기고_접근기록전용조건은뺀다()
    {
        var token = _web.SignIn("la.logins", AllFuncs);

        var json = await JsonAsync(await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, $"/Log/Access/Logins?kind=LOGIN&{Period}&userId=kim&resultCd=FAIL_PWD&programId=SYS_CODE_MGT&ip=10.0.0.1"), Ct));

        Assert.Equal("kim", json.GetProperty("data").GetProperty("items")[0].GetProperty("userId").GetString());
        var p = _web.Sp.LastCall(AccessLogService.LoginListSp).Parameters;
        Assert.Equal(new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Utc), (DateTime)p["P_FROM_DT"]!);
        Assert.Equal(DateTimeKind.Utc, ((DateTime)p["P_TO_DT"]!).Kind);
        Assert.Equal(("kim", "FAIL_PWD", "10.0.0.1"), ((string?)p["P_TARGET_USER_ID"], (string?)p["P_RESULT_CD"], (string?)p["P_IP"]));
        Assert.False(p.ContainsKey("P_PROGRAM_ID")); // 로그인 SP에 없는 파라미터는 보내지 않는다
    }

    [Fact]
    public async Task 접근기록_화면이름을채운다()
    {
        var token = _web.SignIn("la.accesses", AllFuncs);

        var json = await JsonAsync(await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, $"/Log/Access/Accesses?kind=ACCESS&{Period}&programId=IT_NO_MENU&errorYn=Y"), Ct));

        var row = json.GetProperty("data").GetProperty("items")[0];
        Assert.Equal(("IT_NO_MENU", 403), (row.GetProperty("programNm").GetString(), row.GetProperty("statusCode").GetInt32()));
        var p = _web.Sp.LastCall(AccessLogService.AccessListSp).Parameters;
        Assert.Equal(("IT_NO_MENU", "Y"), ((string?)p["P_PROGRAM_ID"], (string?)p["P_ERROR_YN"]));
        Assert.False(p.ContainsKey("P_RESULT_CD"));
    }

    [Fact]
    public async Task 기간이없으면_거부()
    {
        var token = _web.SignIn("la.noperiod", AllFuncs);

        var json = await JsonAsync(await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Log/Access/Logins?kind=LOGIN"), Ct));

        Assert.Equal(LogMessageKeys.Period, json.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task 엑셀_사유를받아개인정보접속기록_권한이없으면403()
    {
        var search = new { kind = "LOGIN", fromDt = "2026-10-01T15:00:00Z", toDt = "2026-10-08T15:00:00Z", userId = "kim" };

        var ok = await PostAsync(_web.SignIn("la.export", AllFuncs), new { search, reason = "보안 점검" });

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ok.Content.Headers.ContentType?.MediaType);
        var pii = _web.Sp.LastCall("LOG.USP_PII_ACCESS_LOG_WRITE").Parameters;
        Assert.Equal((Program, "DOWNLOAD", "보안 점검"), ((string?)pii["P_PROGRAM_ID"], (string?)pii["P_ACTION_CD"], (string?)pii["P_REASON"]));
        Assert.DoesNotContain("kim", (string?)pii["P_TARGET_DESC"] ?? "", StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(_web.SignIn("la.reader2", [Home, $"{Program}:READ"]), new { search, reason = "보안 점검" })).StatusCode);
    }
}
