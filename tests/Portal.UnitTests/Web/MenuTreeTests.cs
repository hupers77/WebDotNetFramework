using Portal.Framework.Web.Navigation;
using Portal.Security.Authorization;

namespace Portal.UnitTests.Web;

public sealed class MenuTreeTests
{
    private static MenuCatalog.MenuRow Row(int id, int? parent, string type, string? program = null, string? url = null, int sort = 0, string scope = "ALL") =>
        new() { MenuId = id, ParentMenuId = parent, MenuTypeCd = type, ProgramId = program, Url = url, SortNo = sort, OpenScopeCd = scope };

    private static IReadOnlyList<MenuNode> Roots(params MenuCatalog.MenuRow[] rows) =>
        MenuCatalog.Build(rows, [.. rows.Select(r => new MenuCatalog.MenuNameRow { MenuId = r.MenuId, LangCd = "ko", MenuNm = $"메뉴{r.MenuId}" })])
            .GetValueOrDefault(MenuSets.Portal) ?? [];

    private static PermissionSet Perm(bool sysAdmin = false, params string[] programs) =>
        new(["R"], DataScopes.Self, sysAdmin, [.. programs.Select(p => PermissionSet.Key(p, "READ"))]);

    private static List<int> Ids(IEnumerable<MenuItem> items) =>
        [.. items.SelectMany(i => new[] { i.MenuId }.Concat(Ids(i.Children)))];

    [Fact]
    public void Build_정렬순서_고아와순환은제외()
    {
        var roots = Roots(
            Row(1, null, "FOLDER", sort: 20),
            Row(2, 1, "PAGE", "P2", "/p2", sort: 2),
            Row(3, 1, "PAGE", "P3", "/p3", sort: 1),
            Row(4, null, "PAGE", "P4", "/p4", sort: 10),
            Row(5, 99, "PAGE", "P5", "/p5"),
            Row(6, 7, "FOLDER"),
            Row(7, 6, "FOLDER"));

        Assert.Equal([4, 1], roots.Select(r => r.MenuId));
        Assert.Equal([3, 2], roots[1].Children.Select(c => c.MenuId));
    }

    [Fact]
    public void 화면은READ권한있을때만_빈폴더는숨김()
    {
        var roots = Roots(Row(1, null, "FOLDER"), Row(2, 1, "PAGE", "P2", "/p2"), Row(3, null, "FOLDER"), Row(4, 3, "PAGE", "P4", "/p4"));

        var items = MenuTreeBuilder.Build(roots, Perm(false, "P2"), isPartner: false, "ko");

        Assert.Equal([1, 2], Ids(items));
    }

    [Theory]
    [InlineData(false, false, new[] { 1, 2, 10, 11 })]
    [InlineData(true, false, new[] { 1, 3, 10, 12 })]
    [InlineData(false, true, new[] { 1, 2, 3, 10, 11, 12 })]
    public void 공개대상_본사협력사_SYS_ADMIN은모두(bool isPartner, bool sysAdmin, int[] expected)
    {
        var roots = Roots(
            Row(1, null, "PAGE", "P1", "/p1"),
            Row(2, null, "PAGE", "P1", "/emp", scope: "EMP"),
            Row(3, null, "PAGE", "P1", "/ptn", scope: "PARTNER"),
            Row(10, null, "FOLDER"),
            Row(11, 10, "PAGE", "P1", "/a", scope: "EMP"),
            Row(12, 10, "PAGE", "P1", "/b", scope: "PARTNER"));

        var items = MenuTreeBuilder.Build(roots, Perm(sysAdmin, "P1"), isPartner, "ko");

        Assert.Equal(expected, Ids(items));
    }

    [Theory]
    [InlineData("LINK", "https://example.com", true)]
    [InlineData("LINK", "http://intranet/manual", true)]
    [InlineData("LINK", "javascript:alert(1)", false)]
    [InlineData("LINK", "//evil.example", false)]
    [InlineData("PAGE", "//evil.example/x", false)]
    [InlineData("PAGE", "https://evil.example/x", false)]
    [InlineData("PAGE", "/ok", true)]
    public void 주소검사_외부링크는http만_화면은사이트안경로만(string type, string url, bool visible)
    {
        var roots = Roots(Row(1, null, type, type == "PAGE" ? "P1" : null, url));

        var items = MenuTreeBuilder.Build(roots, Perm(false, "P1"), isPartner: false, "ko");

        Assert.Equal(visible, items.Count == 1);
    }

    [Fact]
    public void 메뉴명_사용자언어_없으면기본언어()
    {
        var rows = new[] { Row(1, null, "PAGE", "P1", "/p1") };
        var roots = MenuCatalog.Build(rows, [new() { MenuId = 1, LangCd = "ko", MenuNm = "홈" }])[MenuSets.Portal];

        Assert.Equal("홈", MenuTreeBuilder.Build(roots, Perm(false, "P1"), false, "en")[0].Name);
    }
}
