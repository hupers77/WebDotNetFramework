using System.Net;
using System.Text.RegularExpressions;
using Portal.Framework.Web.Context;
using Portal.Framework.Web.Navigation;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>레이아웃 (설계서 12장): 멀티 탭 셸, 권한·공개 대상별 메뉴, 즐겨찾기, 언어 전환, 화면 문구 묶음, 테마.</summary>
public sealed partial class LayoutTests(PortalWebFactory web) : IClassFixture<PortalWebFactory>
{
    private const string Home = "DSH_HOME:READ";
    private const string Screen = TestScreensController.ProgramId + ":READ";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> HtmlAsync(string token, string url, Action<HttpRequestMessage>? setup = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        setup?.Invoke(request);
        var response = await web.NewClient(token).SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private static List<int> TreeMenuIds(string html) =>
        [.. MenuIdPattern().Matches(html[html.IndexOf("class=\"menu-tree\"", StringComparison.Ordinal)..html.IndexOf("id=\"side-fav\"", StringComparison.Ordinal)])
            .Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Distinct()];

    [Fact]
    public async Task 화면직접열기_탭모드_셸이첫탭으로그화면을연다()
    {
        var token = web.SignIn("shell.user", [Home, Screen]);

        var html = await HtmlAsync(token, "/?x=1");

        Assert.Contains("id=\"fw-tabs\"", html, StringComparison.Ordinal);
        Assert.Contains("data-initial-url=\"/?x=1\"", html, StringComparison.Ordinal);
        Assert.Contains("data-max-tabs=\"10\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<h1>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/?_layout=tab", null)]
    [InlineData("/", "iframe")]
    public async Task 탭안요청_본문만(string url, string? fetchDest)
    {
        var token = web.SignIn("tab.user", [Home]);

        var html = await HtmlAsync(token, url, r =>
        {
            if (fetchDest is not null)
            {
                r.Headers.Add("Sec-Fetch-Dest", fetchDest);
            }
        });

        Assert.Contains("<h1>", html, StringComparison.Ordinal);
        Assert.Contains("class=\"in-tab\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"top\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("fw-tabs", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 비밀번호변경강제중_셸없이변경화면()
    {
        var token = web.SignIn("shell.pwd", ["MY_PWD_CHG:READ"], passwordChangeRequired: true);

        var html = await HtmlAsync(token, "/My/Password");

        Assert.DoesNotContain("fw-tabs", html, StringComparison.Ordinal);
        Assert.Contains("name=\"NewPassword\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 메뉴_본사사용자_권한있는화면과외부링크만_빈폴더숨김_잘못된링크제외()
    {
        var token = web.SignIn("menu.emp", [Home, Screen]);

        var ids = TreeMenuIds(await HtmlAsync(token, "/"));

        Assert.Equal([1, 2, 3, 7], ids);
    }

    [Fact]
    public async Task 메뉴_협력사사용자_협력사메뉴보임_본사전용숨김()
    {
        var token = web.SignIn("menu.ptn", [Home, Screen, "SYS_X:READ"], userTypeCd: "PARTNER");

        var ids = TreeMenuIds(await HtmlAsync(token, "/"));

        Assert.Equal([1, 2, 3, 5, 6, 7], ids);
    }

    [Fact]
    public async Task 메뉴_사용자언어로표시()
    {
        var token = web.SignIn("menu.en", [Home, Screen], langCd: "en");

        var html = await HtmlAsync(token, "/");

        Assert.Contains(">Menu3</a>", html, StringComparison.Ordinal);
        Assert.Contains("<html lang=\"en\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 즐겨찾기_표시와저장()
    {
        web.Sp.Returns(MenuService.FavoriteListSp, new List<MenuService.FavoriteRow> { new() { MenuId = 3 }, new() { MenuId = 4 } });
        var token = web.SignIn("fav.user", [Home, Screen]);

        var html = await HtmlAsync(token, "/");
        var (csrf, cookie) = await GetCsrfAsync(web.NewClient(token), "/");
        var request = Ajax(HttpMethod.Post, "/My/Favorite", new { menuId = 1, favorite = true });
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        var response = await web.NewClient(token, cookie).SendAsync(request, Ct);

        // 권한 없는 메뉴(4)는 즐겨찾기에 있어도 보이지 않는다
        var favorites = html[html.IndexOf("id=\"favorite-list\"", StringComparison.Ordinal)..html.IndexOf("id=\"side-recent\"", StringComparison.Ordinal)];
        Assert.Contains("data-menu-id=\"3\"", favorites, StringComparison.Ordinal);
        Assert.DoesNotContain("data-menu-id=\"4\"", favorites, StringComparison.Ordinal);
        Assert.Contains("data-menu-id=\"3\" aria-pressed=\"true\"", html, StringComparison.Ordinal);
        Assert.True((await JsonAsync(response)).GetProperty("success").GetBoolean());
        var call = web.Sp.LastCall(MenuService.FavoriteSaveSp);
        Assert.Equal(1, call.Parameters["P_MENU_ID"]);
        Assert.Equal("Y", call.Parameters["P_FAVORITE_YN"]);
    }

    [Fact]
    public async Task 언어전환_계정에저장_쿠키_돌아갈주소()
    {
        var token = web.SignIn("lang.user", [Home]);
        var (csrf, cookie) = await GetCsrfAsync(web.NewClient(token), "/");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/My/Language")
        {
            Content = new FormUrlEncodedContent([new("__RequestVerificationToken", csrf), new("lang", "en"), new("returnUrl", "/test-screen/page")]),
        };
        var response = await web.NewClient(token, cookie).SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/test-screen/page", response.Headers.Location!.OriginalString);
        Assert.Equal("en", web.Sp.LastCall(UserLanguageService.SaveSp).Parameters["P_NEW_LANG_CD"]);
        Assert.StartsWith($"{HttpCurrentUser.LangCookie}=en", SetCookie(response, HttpCurrentUser.LangCookie), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 언어전환_로그인전_쿠키만_로그인화면언어바뀜()
    {
        var (csrf, cookie) = await GetCsrfAsync(web.NewClient(), "/Account/Login");
        var calls = web.Sp.Calls.Count(c => c.SpName == UserLanguageService.SaveSp);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Language")
        {
            Content = new FormUrlEncodedContent([new("__RequestVerificationToken", csrf), new("lang", "zh-CN")]),
        };
        var response = await web.NewClient(otherCookie: cookie).SendAsync(request, Ct);
        var login = await web.NewClient(otherCookie: $"{HttpCurrentUser.LangCookie}=zh-CN").GetStringAsync("/Account/Login", Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.Equal(calls, web.Sp.Calls.Count(c => c.SpName == UserLanguageService.SaveSp));
        Assert.Contains("<html lang=\"zh-CN\">", login, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 화면문구묶음_사용자언어_없으면기본언어_버전맞으면장기캐시()
    {
        var client = web.NewClient();
        var page = await client.GetStringAsync("/Account/Login", Ct);
        var bundleUrl = BundlePattern().Match(page).Groups[1].Value.Replace("ko.json", "en.json", StringComparison.Ordinal);

        var response = await client.GetAsync(bundleUrl, Ct);
        var stale = await client.GetAsync("/i18n/en.json?v=old", Ct);
        var unknown = await client.GetAsync("/i18n/xx.json", Ct);

        var json = await JsonAsync(response);
        Assert.Equal("You do not have permission.", json.GetProperty("MSG.FORBIDDEN").GetString());
        Assert.Equal("로그인이 필요합니다.", json.GetProperty("MSG.LOGIN_REQUIRED").GetString());
        Assert.Contains("immutable", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.True(stale.Headers.CacheControl!.NoCache);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task 테마CSS_브랜드색()
    {
        var css = await web.NewClient().GetStringAsync("/theme.css", Ct);

        Assert.Equal(":root { --fw-brand: #1F5FBF; }\n", css);
    }

    [GeneratedRegex("data-menu-id=\"(\\d+)\"")]
    private static partial Regex MenuIdPattern();

    [GeneratedRegex("""name="fw-i18n" content="([^"]+)" """)]
    private static partial Regex BundlePattern();
}

/// <summary>단일 화면 모드 (<c>UI.TAB_MODE = N</c>): 셸 없이 화면을 레이아웃 안에 바로 그린다.</summary>
public sealed class SingleScreenModeTests : IDisposable
{
    private readonly PortalWebFactory _web = new();

    public SingleScreenModeTests() => _web.Config.Set("UI.TAB_MODE", "N");

    [Fact]
    public async Task 탭없이본문을바로그림()
    {
        var token = _web.SignIn("single.user", ["DSH_HOME:READ"]);

        var html = await _web.NewClient(token).GetStringAsync("/", TestContext.Current.CancellationToken);

        Assert.DoesNotContain("fw-tabs", html, StringComparison.Ordinal);
        Assert.Contains("class=\"top\"", html, StringComparison.Ordinal);
        Assert.Contains("<h1>", html, StringComparison.Ordinal);
    }

    public void Dispose() => _web.Dispose();
}
