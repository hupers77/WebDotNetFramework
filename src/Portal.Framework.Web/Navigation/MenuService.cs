using Microsoft.Extensions.Caching.Memory;
using Portal.Core.Context;
using Portal.Data;
using Portal.Framework.Web.Authorization;
using Portal.Security.Authorization;

namespace Portal.Framework.Web.Navigation;

/// <summary>현재 사용자의 메뉴 트리와 즐겨찾기 (레이아웃 좌측 메뉴).</summary>
public sealed class MenuService(MenuCatalog catalog, CurrentPermissions permissions, ICurrentUser currentUser, ISpExecutor sp, IMemoryCache cache)
{
    internal const string FavoriteListSp = "FW.USP_USER_FAVORITE_GET_LIST";
    internal const string FavoriteSaveSp = "FW.USP_USER_FAVORITE_SAVE";

    // 즐겨찾기는 바꿀 때만 DB에 간다. 웹서버가 환경마다 1대라 저장 시 키 삭제로 충분하다.
    private static readonly MemoryCacheEntryOptions FavoriteOptions = new() { SlidingExpiration = TimeSpan.FromMinutes(30), Size = 1 };

    /// <summary>보이는 메뉴 트리.</summary>
    public async Task<IReadOnlyList<MenuItem>> GetTreeAsync(string menuSetCd = MenuSets.Portal, CancellationToken cancellationToken = default)
    {
        var granted = await permissions.GetAsync(cancellationToken);
        return MenuTreeBuilder.Build(catalog.Roots(menuSetCd), granted, currentUser.UserTypeCd == PermissionService.PartnerUserType, currentUser.LangCd);
    }

    /// <summary>즐겨찾기 (지금 보이는 메뉴만, 등록 순서).</summary>
    public async Task<IReadOnlyList<MenuItem>> GetFavoritesAsync(IReadOnlyList<MenuItem> tree, CancellationToken cancellationToken = default)
    {
        var ids = await FavoriteIdsAsync(cancellationToken);
        var pages = MenuTreeBuilder.Pages(tree).GroupBy(m => m.MenuId).ToDictionary(g => g.Key, g => g.First());
        return [.. ids.Where(pages.ContainsKey).Select(id => pages[id])];
    }

    /// <summary>즐겨찾기 추가·해제.</summary>
    public async Task SetFavoriteAsync(int menuId, bool favorite, CancellationToken cancellationToken = default)
    {
        await sp.ExecuteAsync(FavoriteSaveSp, new { P_MENU_ID = menuId, P_FAVORITE_YN = favorite ? "Y" : "N" }, cancellationToken);
        cache.Remove(FavoriteKey(currentUser.UserId));
    }

    private async Task<IReadOnlyList<int>> FavoriteIdsAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return [];
        }

        if (cache.TryGetValue(FavoriteKey(userId), out IReadOnlyList<int>? cached) && cached is not null)
        {
            return cached;
        }

        var rows = await sp.QueryAsync<FavoriteRow>(FavoriteListSp, null, cancellationToken);
        IReadOnlyList<int> ids = [.. rows.Select(r => r.MenuId)];
        cache.Set(FavoriteKey(userId), ids, FavoriteOptions);
        return ids;
    }

    private static (string, string?) FavoriteKey(string? userId) => ("menu.favorite", userId);

    internal sealed class FavoriteRow
    {
        public int MenuId { get; set; }
    }
}
