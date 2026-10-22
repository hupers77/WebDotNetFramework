using System.Collections.Frozen;
using Microsoft.Extensions.DependencyInjection;
using Portal.Core.Keys;
using Portal.Data;
using Portal.Infrastructure.Caching;

namespace Portal.Framework.Web.Navigation;

/// <summary>
/// 메뉴 트리 캐시 (설계서 10장: 메뉴 트리는 서버 메모리, 캐시 버전 <c>MENU</c>). 모든 메뉴 세트와 언어별 메뉴명을 들고 있고,
/// 사용자별 노출은 <see cref="MenuTreeBuilder"/>가 권한으로 거른다.
/// </summary>
public sealed class MenuCatalog(IServiceScopeFactory scopeFactory) : ICacheVersionListener
{
    internal const string ListSp = "FW.USP_MENU_GET_LIST";

    private volatile FrozenDictionary<string, IReadOnlyList<MenuNode>> _roots = FrozenDictionary<string, IReadOnlyList<MenuNode>>.Empty;

    private volatile FrozenDictionary<string, IReadOnlyDictionary<string, string>> _programNames =
        FrozenDictionary<string, IReadOnlyDictionary<string, string>>.Empty;

    /// <inheritdoc />
    public string CacheKey => CacheKeys.Menu;

    /// <summary>메뉴 세트의 최상위 메뉴 (정렬 순서). 없는 세트면 빈 목록.</summary>
    public IReadOnlyList<MenuNode> Roots(string menuSetCd) => _roots.GetValueOrDefault(menuSetCd) ?? [];

    /// <summary>
    /// 프로그램의 화면 제목 = 그 프로그램을 연 메뉴의 이름 (웹 접근성: 탭 제목·<c>&lt;title&gt;</c>·<c>&lt;h1&gt;</c>에 메뉴명).
    /// 사용자 언어 → 기본 언어 → 프로그램 ID 순으로 대체한다.
    /// </summary>
    public string ProgramTitle(string programId, string langCd) =>
        _programNames.GetValueOrDefault(programId) is { } names
            ? names.GetValueOrDefault(langCd) ?? names.GetValueOrDefault(Infrastructure.Localization.LangResources.DefaultLang) ?? programId
            : programId;

    /// <inheritdoc />
    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider.GetRequiredService<ISpExecutor>();
        var (rows, names) = await sp.QueryMultipleAsync(
            ListSp,
            null,
            async reader => (await reader.ReadAsync<MenuRow>(cancellationToken), await reader.ReadAsync<MenuNameRow>(cancellationToken)),
            cancellationToken);
        var roots = Build(rows, names);
        _programNames = IndexPrograms(roots);
        _roots = roots;
    }

    internal static FrozenDictionary<string, IReadOnlyList<MenuNode>> Build(IReadOnlyList<MenuRow> rows, IReadOnlyList<MenuNameRow> names)
    {
        var namesById = names
            .GroupBy(n => n.MenuId)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, string>)g.ToFrozenDictionary(n => n.LangCd, n => n.MenuNm, StringComparer.Ordinal));
        var children = rows.ToLookup(r => r.ParentMenuId);
        var ids = rows.Select(r => r.MenuId).ToHashSet();

        MenuNode ToNode(MenuRow row, HashSet<int> path)
        {
            // 부모를 잘못 지정해 생긴 순환은 끊는다 (메뉴 관리 화면도 막지만 캐시가 무한 루프에 빠지지 않게)
            path.Add(row.MenuId);
            var kids = children[row.MenuId]
                .Where(c => !path.Contains(c.MenuId))
                .OrderBy(c => c.SortNo).ThenBy(c => c.MenuId)
                .Select(c => ToNode(c, path))
                .ToList();
            path.Remove(row.MenuId);
            return new MenuNode(
                row.MenuId, row.MenuTypeCd, row.ProgramId, row.Url, row.IconCd, row.NewWinYn == "Y", row.OpenScopeCd,
                namesById.GetValueOrDefault(row.MenuId) ?? FrozenDictionary<string, string>.Empty, kids);
        }

        // 부모가 사용 중지되어 빠진 메뉴는 고아가 되므로 보이지 않는다 (상위 폴더를 끄면 하위도 숨김)
        return rows
            .Where(r => r.ParentMenuId is null)
            .GroupBy(r => r.MenuSetCd)
            .ToFrozenDictionary(
                g => g.Key,
                g => (IReadOnlyList<MenuNode>)[.. g.OrderBy(r => r.SortNo).ThenBy(r => r.MenuId).Select(r => ToNode(r, []))],
                StringComparer.Ordinal);
    }

    internal static FrozenDictionary<string, IReadOnlyDictionary<string, string>> IndexPrograms(FrozenDictionary<string, IReadOnlyList<MenuNode>> roots)
    {
        var index = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        void Visit(MenuNode node)
        {
            // 같은 프로그램을 여러 메뉴에 걸면 정렬상 먼저 나오는 메뉴의 이름을 쓴다
            if (node.ProgramId is { } id && node.MenuTypeCd is MenuTypes.Page or MenuTypes.Popup)
            {
                index.TryAdd(id, node.Names);
            }

            foreach (var child in node.Children)
            {
                Visit(child);
            }
        }

        foreach (var node in roots.Values.SelectMany(r => r))
        {
            Visit(node);
        }

        return index.ToFrozenDictionary(StringComparer.Ordinal);
    }

    internal sealed class MenuRow
    {
        public int MenuId { get; set; }

        public string MenuSetCd { get; set; } = MenuSets.Portal;

        public int? ParentMenuId { get; set; }

        public string MenuTypeCd { get; set; } = "";

        public string? ProgramId { get; set; }

        public string? Url { get; set; }

        public string? IconCd { get; set; }

        public int SortNo { get; set; }

        public string NewWinYn { get; set; } = "N";

        public string OpenScopeCd { get; set; } = MenuScopes.All;
    }

    internal sealed class MenuNameRow
    {
        public int MenuId { get; set; }

        public string LangCd { get; set; } = "";

        public string MenuNm { get; set; } = "";
    }
}

/// <summary>캐시된 메뉴 (사용자와 무관한 전체 트리).</summary>
/// <param name="MenuId">메뉴 ID.</param>
/// <param name="MenuTypeCd">FOLDER/PAGE/LINK/POPUP (<see cref="MenuTypes"/>).</param>
/// <param name="ProgramId">연결 프로그램 (PAGE/POPUP).</param>
/// <param name="Url">프로그램 URL 또는 외부 링크.</param>
/// <param name="IconCd">아이콘 코드.</param>
/// <param name="NewWindow">새 창으로 열지.</param>
/// <param name="OpenScopeCd">공개 대상 ALL/EMP/PARTNER (<see cref="MenuScopes"/>).</param>
/// <param name="Names">언어별 메뉴명.</param>
/// <param name="Children">하위 메뉴 (정렬 순서).</param>
public sealed record MenuNode(
    int MenuId,
    string MenuTypeCd,
    string? ProgramId,
    string? Url,
    string? IconCd,
    bool NewWindow,
    string OpenScopeCd,
    IReadOnlyDictionary<string, string> Names,
    IReadOnlyList<MenuNode> Children);

/// <summary>메뉴 세트 코드.</summary>
public static class MenuSets
{
    /// <summary>포털 기본 메뉴.</summary>
    public const string Portal = "PORTAL";
}

/// <summary>메뉴 유형 코드.</summary>
public static class MenuTypes
{
    /// <summary>묶음.</summary>
    public const string Folder = "FOLDER";

    /// <summary>프로그램 화면.</summary>
    public const string Page = "PAGE";

    /// <summary>외부 URL.</summary>
    public const string Link = "LINK";

    /// <summary>팝업 화면.</summary>
    public const string Popup = "POPUP";
}

/// <summary>메뉴 공개 대상 코드 (설계서 6장 '회사 간 공개와 격리').</summary>
public static class MenuScopes
{
    /// <summary>모든 회사.</summary>
    public const string All = "ALL";

    /// <summary>본사 사용자만.</summary>
    public const string Employee = "EMP";

    /// <summary>협력사 사용자만.</summary>
    public const string Partner = "PARTNER";
}
