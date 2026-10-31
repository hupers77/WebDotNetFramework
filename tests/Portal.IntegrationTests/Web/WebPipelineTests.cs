using System.Net;
using Portal.Framework.Web.Authentication;
using Portal.Security.Authentication;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>보안 헤더, 인증·권한 거부, 공통 응답 형식, 접근 기록 (설계서 4·5·6·9장).</summary>
public sealed class WebPipelineTests(PortalWebFactory web) : IClassFixture<PortalWebFactory>
{
    private const string Read = TestScreensController.ProgramId + ":READ";
    private const string Update = TestScreensController.ProgramId + ":UPDATE";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task 모든응답_보안헤더포함()
    {
        var response = await web.NewClient().GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'self'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task 화면_비로그인_로그인화면으로()
    {
        var response = await web.NewClient().GetAsync("/test-screen/page?x=1", Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login?returnUrl=%2Ftest-screen%2Fpage%3Fx%3D1", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Ajax_비로그인_401Json()
    {
        var response = await web.NewClient().SendAsync(Ajax(HttpMethod.Get, "/test-screen/list"), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var json = await JsonAsync(response);
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal("MSG.LOGIN_REQUIRED", json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("로그인이 필요합니다.", json.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Ajax_권한없음_403Json_사용자언어로()
    {
        var token = web.SignIn("no.perm.en", [], langCd: "en");

        var response = await web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/test-screen/list"), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var json = await JsonAsync(response);
        Assert.Equal("MSG.FORBIDDEN", json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("You do not have permission.", json.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Ajax_조회권한_성공응답과추적ID()
    {
        var token = web.SignIn("reader", [Read]);

        var response = await web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/test-screen/list"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await JsonAsync(response);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(2, json.GetProperty("data").GetArrayLength());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task 프로그램미등록_Controller_기본거부()
    {
        var token = web.SignIn("reader2", [Read, Update], roles: ["PORTAL_ADMIN"]);

        var response = await web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/test-unregistered"), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 화면_권한없음_403오류화면()
    {
        var token = web.SignIn("no.perm", []);

        var response = await web.NewClient(token).GetAsync("/test-screen/page", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("LBL.FORBIDDEN_TITLE", html, StringComparison.Ordinal);
        Assert.Contains("권한이 없습니다.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 변경요청_CSRF토큰없음_400()
    {
        var token = web.SignIn("writer.nocsrf", [Read, Update]);

        var response = await web.NewClient(token).SendAsync(Ajax(HttpMethod.Post, "/test-screen/save", new { code = "A" }), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task 변경요청_수정권한없음_403()
    {
        var token = web.SignIn("reader.only", [Read, "DSH_HOME:READ"]);
        var (csrf, cookie) = await GetCsrfAsync(web.NewClient(token), "/");

        var request = Ajax(HttpMethod.Post, "/test-screen/save", new { code = "A" });
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        var response = await web.NewClient(token, cookie).SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 변경요청_업무오류_200실패응답()
    {
        var token = web.SignIn("writer", [Read, Update, "DSH_HOME:READ"]);
        var (csrf, cookie) = await GetCsrfAsync(web.NewClient(token), "/");

        var request = Ajax(HttpMethod.Post, "/test-screen/save", new { code = "DUP" });
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        var response = await web.NewClient(token, cookie).SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await JsonAsync(response);
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal("MSG.DUP_CODE", json.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("이미 사용 중인 코드입니다.", json.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task 형식이틀린JSON_내부형식이름없이_입력오류로()
    {
        var token = web.SignIn("writer.badjson", [Read, Update, "DSH_HOME:READ"]);
        var (csrf, cookie) = await GetCsrfAsync(web.NewClient(token), "/");

        var request = Ajax(HttpMethod.Post, "/test-screen/save");
        request.Content = new StringContent("{\"code\": 123", System.Text.Encoding.UTF8, "application/json");
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        var response = await web.NewClient(token, cookie).SendAsync(request, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("MSG.INVALID_INPUT", body, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("LineNumber", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 상태확인은_외부에서_버전을_알려주지않는다()
    {
        var body = await (await web.NewClient().GetAsync("/health", Ct)).Content.ReadAsStringAsync(Ct);

        Assert.Contains("Healthy", body, StringComparison.Ordinal);
        Assert.DoesNotContain("version", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task 처리되지않은예외_500_내부정보숨김()
    {
        var token = web.SignIn("boom", [Read]);

        var ajax = await web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, "/test-screen/boom"), Ct);
        var page = await web.NewClient(token).GetAsync("/test-screen/boom", Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, ajax.StatusCode);
        Assert.Equal("MSG.SYSTEM_ERROR", (await JsonAsync(ajax)).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.InternalServerError, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("민감한 내부 정보", html, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 다른곳로그인으로종료된세션_쿠키삭제후사유와함께로그인화면()
    {
        var token = web.SignIn("kicked", [Read]);
        web.EndSession(token, LoginResultCodes.Kicked);

        var response = await web.NewClient(token).GetAsync("/test-screen/page", Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("reason=AUTH.SESSION_KICKED", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.Contains("expires=Thu, 01 Jan 1970", SetCookie(response, SessionCookie.Name), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task 비밀번호변경필요_다른화면은변경화면으로_AJAX는403()
    {
        var token = web.SignIn("must.change", [Read, "MY_PWD_CHG:READ"], passwordChangeRequired: true);
        var client = web.NewClient(token);

        var page = await client.GetAsync("/test-screen/page", Ct);
        var ajax = await client.SendAsync(Ajax(HttpMethod.Get, "/test-screen/list"), Ct);
        var password = await client.GetAsync("/My/Password", Ct);

        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Equal("/My/Password", page.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Forbidden, ajax.StatusCode);
        Assert.Equal("AUTH.PWD_CHANGE_REQUIRED", (await JsonAsync(ajax)).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, password.StatusCode);
    }

    [Fact]
    public async Task 접근기록_사용자프로그램경로상태_쿼리문자열제외_정적파일제외()
    {
        var token = web.SignIn("logged", [Read]);
        var client = web.NewClient(token);
        web.DrainAccessLog();

        await client.SendAsync(Ajax(HttpMethod.Get, "/test-screen/list?keyword=secret"), Ct);
        await client.GetAsync("/css/portal.css", Ct);
        var entries = web.DrainAccessLog();

        var entry = Assert.Single(entries);
        Assert.Equal("logged", entry.UserId);
        Assert.Equal(TestScreensController.ProgramId, entry.ProgramId);
        Assert.Equal("/test-screen/list", entry.RequestPath);
        Assert.Equal("GET", entry.HttpMethod);
        Assert.Equal(200, entry.StatusCode);
        Assert.False(string.IsNullOrEmpty(entry.TraceId));
    }

    [Fact]
    public async Task 접근기록_오류화면재실행은한번만_예외는500()
    {
        var token = web.SignIn("logged.error", [Read]);
        var client = web.NewClient(token);
        web.DrainAccessLog();

        await client.GetAsync("/no-such-page", Ct);
        await client.GetAsync("/test-screen/boom", Ct);
        var entries = web.DrainAccessLog();

        Assert.Equal(["/no-such-page:404", "/test-screen/boom:500"], entries.Select(e => $"{e.RequestPath}:{e.StatusCode}"));
    }
}
