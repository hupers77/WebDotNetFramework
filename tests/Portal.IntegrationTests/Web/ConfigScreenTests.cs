using System.Net;
using Portal.Core.Results;
using Portal.Data;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>설정 관리 화면: 권한별 버튼, 수정만 받는 저장, 비밀값을 내보내지 않음.</summary>
public sealed class ConfigScreenTests : IClassFixture<PortalWebFactory>
{
    private const string Program = "SYS_CONFIG_MGT";
    private const string Home = "DSH_HOME:READ";
    private static readonly string[] AllFuncs = [Home, $"{Program}:READ", $"{Program}:UPDATE"];
    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 1];
    private readonly PortalWebFactory _web;

    public ConfigScreenTests(PortalWebFactory web)
    {
        _web = web;
        web.Sp.Returns(CodeAdminService.ListSp, new PageResult<CodeDto>
        {
            Items = [new() { Code = "MAIL", CodeNm = "메일", UseYn = "Y" }, new() { Code = "OLD", CodeNm = "안씀", UseYn = "N" }],
            TotalCount = 2,
            PageNo = 1,
            PageSize = CodeAdminService.MaxRows,
        });
        web.Sp.Returns(CodeAdminService.NameListSp, new List<CodeAdminService.NameRow>());
        web.Sp.Returns(ConfigAdminService.ListSp, new PageResult<ConfigDto>
        {
            Items =
            [
                new() { ConfigKey = "MAIL.SMTP_HOST", ConfigVal = "smtp.local", ValTypeCd = "STRING", CategoryCd = "MAIL", RowVer = RowVer },
                new() { ConfigKey = "MAIL.SMTP_PASSWORD", ValTypeCd = "SECRET", SecretSetYn = "Y", CategoryCd = "MAIL", RestartYn = "Y", RowVer = RowVer },
            ],
            TotalCount = 2,
            PageNo = 1,
            PageSize = ConfigAdminService.MaxRows,
        });
        web.Sp.Returns(ConfigAdminService.SaveSp, p => new SaveResult(((Tvp)p["P_ROWS"]!).Table.Rows.Count, null));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> PageAsync(string token)
    {
        var response = await _web.NewClient(token).GetAsync("/Sys/Config?_layout=tab", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private async Task<HttpResponseMessage> SaveAsync(string token, params object[] rows)
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(token), "/");
        var request = Ajax(HttpMethod.Post, "/Sys/Config/Save", new { rows });
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await _web.NewClient(token, cookie).SendAsync(request, Ct);
    }

    private static object Row(string rowStat, string key, string value) => new { rowStat, configKey = key, configVal = value, rowVer = "AAAAAAAAAAE=" };

    [Fact]
    public async Task 화면_수정권한_저장버튼과_사용중인분류만()
    {
        var html = await PageAsync(_web.SignIn("config.admin", AllFuncs));

        Assert.Contains("id=\"config-save\"", html, StringComparison.Ordinal);
        Assert.Contains("<option value=\"MAIL\">메일</option>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"OLD\"", html, StringComparison.Ordinal);
        Assert.Contains("js/sys/config.js", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 화면_조회권한만_저장버튼을그리지않는다()
    {
        var html = await PageAsync(_web.SignIn("config.reader", [Home, $"{Program}:READ"]));

        Assert.DoesNotContain("id=\"config-save\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 조회권한없음_화면목록이력모두403()
    {
        var token = _web.SignIn("config.none", [Home]);

        var page = await _web.NewClient(token).GetAsync("/Sys/Config?_layout=tab", Ct);
        var list = await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Sys/Config/List"), Ct);
        var history = await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Sys/Config/History?configKey=MAIL.SMTP_HOST"), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, page.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, history.StatusCode);
    }

    [Fact]
    public async Task 목록_비밀값은_입력여부만()
    {
        var token = _web.SignIn("config.list", AllFuncs);

        var response = await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/Sys/Config/List?categoryCd=MAIL&keyword=%20smtp%20"), Ct);

        var items = (await JsonAsync(response)).GetProperty("data").GetProperty("items");
        var secret = items[1];
        Assert.Equal("Y", secret.GetProperty("secretSetYn").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, secret.GetProperty("configVal").ValueKind);
        var call = _web.Sp.LastCall(ConfigAdminService.ListSp);
        Assert.Equal(("MAIL", "smtp"), (call.Parameters["P_CATEGORY_CD"], call.Parameters["P_KEYWORD"]));
    }

    [Fact]
    public async Task 저장_비밀값은_암호화해보내고_재시작안내()
    {
        var token = _web.SignIn("config.saver", AllFuncs);

        var response = await SaveAsync(token, Row("U", "MAIL.SMTP_PASSWORD", "new-secret"));

        var data = (await JsonAsync(response)).GetProperty("data");
        Assert.Equal(1, data.GetProperty("affectedCnt").GetInt32());
        Assert.True(data.GetProperty("restartRequired").GetBoolean());
        var stored = (string)((Tvp)_web.Sp.LastCall(ConfigAdminService.SaveSp).Parameters["P_ROWS"]!).Table.Rows[0]["CONFIG_VAL"];
        Assert.DoesNotContain("new-secret", stored, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 저장_수정권한없음_403()
    {
        var token = _web.SignIn("config.reader2", [Home, $"{Program}:READ"]);

        var response = await SaveAsync(token, Row("U", "MAIL.SMTP_HOST", "a"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 저장_추가행_입력오류_SP호출안함()
    {
        var token = _web.SignIn("config.insert", AllFuncs);
        var before = _web.Sp.Calls.Count(c => c.SpName == ConfigAdminService.SaveSp);

        var response = await SaveAsync(token, Row("I", "MAIL.SMTP_HOST", "a"));

        // 이 화면은 추가 기능(CREATE)이 없으므로 행 권한 검사에서 먼저 막힌다
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, _web.Sp.Calls.Count(c => c.SpName == ConfigAdminService.SaveSp));
    }
}
