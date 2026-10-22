using Microsoft.Extensions.DependencyInjection;
using Portal.Core.Errors;
using Portal.Data;
using Portal.Framework.Web.Navigation;
using Portal.IntegrationTests.Infrastructure;

namespace Portal.IntegrationTests.Fw;

/// <summary>메뉴 트리·즐겨찾기·언어 변경 SP (레이아웃).</summary>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class MenuSpTests(DbFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<MenuCatalog> LoadCatalogAsync(TestDb db)
    {
        var services = new ServiceCollection().AddSingleton(db.Sp).BuildServiceProvider();
        var catalog = new MenuCatalog(services.GetRequiredService<IServiceScopeFactory>());
        await catalog.ReloadAsync(Ct);
        return catalog;
    }

    [Fact]
    public async Task 메뉴목록_초기데이터트리_프로그램URL_3개언어이름()
    {
        await using var db = await fixture.BeginAsync();

        var roots = (await LoadCatalogAsync(db)).Roots(MenuSets.Portal);

        var home = Assert.Single(roots, r => r.ProgramId == "DSH_HOME");
        Assert.Equal("/", home.Url);
        Assert.Equal(["en", "ko", "zh-CN"], home.Names.Keys.Order(StringComparer.Ordinal));
        var my = Assert.Single(roots, r => r.Children.Any(c => c.ProgramId == "MY_PWD_CHG"));
        Assert.Equal("/My/Password", Assert.Single(my.Children, c => c.ProgramId == "MY_PWD_CHG").Url);
        Assert.Equal(roots.OrderBy(r => r.MenuId).Count(), roots.Count);
    }

    [Fact]
    public async Task 메뉴목록_사용중지메뉴와사용중지프로그램은제외_외부링크주소()
    {
        await using var db = await fixture.BeginAsync();
        await db.ExecAsync("UPDATE FW.TB_PROGRAM SET USE_YN = 'N' WHERE PROGRAM_ID = 'MY_PWD_CHG'");
        await db.ExecAsync("UPDATE FW.TB_MENU SET USE_YN = 'N' WHERE MENU_KEY = 'BRD_NOTICE'");
        await db.ExecAsync("INSERT INTO FW.TB_MENU (MENU_KEY, MENU_TYPE_CD, LINK_URL, SORT_NO, NEW_WIN_YN) VALUES ('IT_LINK', 'LINK', 'https://example.com/manual', 999, 'Y')");

        var roots = (await LoadCatalogAsync(db)).Roots(MenuSets.Portal);

        var all = Flatten(roots).ToList();
        Assert.DoesNotContain(all, n => n.ProgramId is "MY_PWD_CHG" or "BRD_NOTICE");
        var link = Assert.Single(all, n => n.MenuTypeCd == MenuTypes.Link && n.Url == "https://example.com/manual");
        Assert.True(link.NewWindow);
    }

    [Fact]
    public async Task 즐겨찾기_추가는한번만_해제_목록()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedUserAsync("it.fav", "HQ", "EMP");
        db.AsUser("it.fav", "HQ", "EMP");
        var notice = await db.ScalarAsync<int>("SELECT MENU_ID FROM FW.TB_MENU WHERE MENU_KEY = 'BRD_NOTICE'");
        var qna = await db.ScalarAsync<int>("SELECT MENU_ID FROM FW.TB_MENU WHERE MENU_KEY = 'BRD_QNA'");

        await SaveFavoriteAsync(db, notice, "Y");
        var again = await SaveFavoriteAsync(db, notice, "Y");
        await SaveFavoriteAsync(db, qna, "Y");
        var before = await FavoritesAsync(db);
        await SaveFavoriteAsync(db, notice, "N");

        Assert.Equal(0, again.AffectedCnt);
        Assert.Equal([notice, qna], before);
        Assert.Equal([qna], await FavoritesAsync(db));
    }

    [Fact]
    public async Task 즐겨찾기_폴더는추가불가()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedUserAsync("it.fav2", "HQ", "EMP");
        db.AsUser("it.fav2", "HQ", "EMP");
        var folder = await db.ScalarAsync<int>("SELECT MENU_ID FROM FW.TB_MENU WHERE MENU_KEY = 'SYS'");

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveFavoriteAsync(db, folder, "Y"));

        Assert.Equal("MSG.NOT_FOUND", ex.MessageKey);
    }

    [Fact]
    public async Task 언어변경_계정에저장_감사로그()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedUserAsync("it.lang", "HQ", "EMP");
        db.AsUser("it.lang", "HQ", "EMP");

        await db.Sp.ExecuteAsync("FW.USP_USER_LANG_SAVE", new { P_NEW_LANG_CD = "en" }, Ct);

        Assert.Equal("en", await db.ScalarAsync<string>("SELECT LANG_CD FROM FW.TB_USER WHERE USER_ID = 'it.lang'"));
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_USER' AND ROW_KEY = 'it.lang' AND AFTER_JSON LIKE '%\"en\"%'"));
    }

    [Fact]
    public async Task 언어변경_사용하지않는언어_거부()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedUserAsync("it.lang2", "HQ", "EMP");
        db.AsUser("it.lang2", "HQ", "EMP");

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync("FW.USP_USER_LANG_SAVE", new { P_NEW_LANG_CD = "fr" }, Ct));

        Assert.Equal("MSG.INVALID_INPUT", ex.MessageKey);
    }

    [Fact]
    public async Task 언어목록_사용중인언어_정렬순서()
    {
        await using var db = await fixture.BeginAsync();

        var langs = await db.ColumnAsync<string>("EXEC FW.USP_LANG_GET_LIST @P_USER_ID = NULL, @P_LANG_CD = 'ko'");

        Assert.Equal(["ko", "en", "zh-CN"], langs);
    }

    private static Task<SaveResult> SaveFavoriteAsync(TestDb db, int menuId, string yn) =>
        db.Sp.ExecuteAsync("FW.USP_USER_FAVORITE_SAVE", new { P_MENU_ID = menuId, P_FAVORITE_YN = yn }, Ct);

    private static async Task<List<int>> FavoritesAsync(TestDb db) =>
        [.. (await db.Sp.QueryAsync<FavoriteRow>("FW.USP_USER_FAVORITE_GET_LIST", null, Ct)).Select(r => r.MenuId)];

    private static IEnumerable<MenuNode> Flatten(IEnumerable<MenuNode> nodes) =>
        nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));

    private sealed class FavoriteRow
    {
        public int MenuId { get; set; }
    }
}
