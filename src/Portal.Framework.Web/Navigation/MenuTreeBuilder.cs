using Portal.Core.Keys;
using Portal.Infrastructure.Localization;
using Portal.Security.Authorization;

namespace Portal.Framework.Web.Navigation;

/// <summary>
/// 사용자에게 보일 메뉴를 고른다 (설계서 6장 권한 평가 규칙 4). 서버 권한 검사와 같은 <see cref="PermissionSet"/>을 쓰므로
/// 메뉴에 보이는 것과 서버가 허용하는 것이 항상 같다.
/// <list type="bullet">
/// <item>PAGE/POPUP: 프로그램에 <c>READ</c> 권한이 있을 때.</item>
/// <item>LINK: 공개 대상이 맞고 http(s) 주소일 때 (프로그램이 없으므로 권한 대상이 아니다).</item>
/// <item>FOLDER: 하위에 보이는 메뉴가 하나 이상 있을 때.</item>
/// <item>공개 대상: <c>EMP</c>는 협력사 사용자에게, <c>PARTNER</c>는 본사 사용자에게 숨긴다(SYS_ADMIN은 모두 본다). 숨긴 폴더의 하위도 숨긴다.</item>
/// </list>
/// </summary>
public static class MenuTreeBuilder
{
    /// <summary>보이는 메뉴 트리를 만든다. 메뉴명은 사용자 언어 → 기본 언어 → 메뉴 ID 순.</summary>
    public static IReadOnlyList<MenuItem> Build(IReadOnlyList<MenuNode> roots, PermissionSet permissions, bool isPartner, string langCd)
    {
        var result = new List<MenuItem>();
        foreach (var node in roots)
        {
            if (Visible(node, permissions, isPartner, langCd) is { } item)
            {
                result.Add(item);
            }
        }

        return result;
    }

    private static MenuItem? Visible(MenuNode node, PermissionSet permissions, bool isPartner, string langCd)
    {
        if (!ScopeAllows(node.OpenScopeCd, permissions, isPartner))
        {
            return null;
        }

        var children = Build(node.Children, permissions, isPartner, langCd);
        var visible = node.MenuTypeCd switch
        {
            MenuTypes.Folder => children.Count > 0,
            MenuTypes.Link => IsExternalUrl(node.Url),
            _ => node.ProgramId is not null && IsLocalPath(node.Url) && permissions.Can(node.ProgramId, FuncCodes.Read),
        };
        if (!visible)
        {
            return null;
        }

        var name = node.Names.GetValueOrDefault(langCd) ?? node.Names.GetValueOrDefault(LangResources.DefaultLang) ?? node.MenuId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new MenuItem(node.MenuId, name, node.MenuTypeCd, node.ProgramId, node.Url, node.IconCd, node.NewWindow, children);
    }

    // 메뉴 관리 화면에서 잘못 넣은 javascript: 등은 링크로 만들지 않는다
    private static bool IsExternalUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static bool IsLocalPath(string? url) =>
        url is { Length: > 0 } && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));

    private static bool ScopeAllows(string openScopeCd, PermissionSet permissions, bool isPartner) => openScopeCd switch
    {
        MenuScopes.All => true,
        _ when permissions.IsSysAdmin => true,
        MenuScopes.Employee => !isPartner,
        MenuScopes.Partner => isPartner,
        _ => false,
    };

    /// <summary>트리를 펼친 화면 메뉴 (FOLDER 제외). 즐겨찾기·검색·현재 위치 찾기에 쓴다.</summary>
    public static IEnumerable<MenuItem> Pages(IEnumerable<MenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.MenuTypeCd != MenuTypes.Folder)
            {
                yield return item;
            }

            foreach (var child in Pages(item.Children))
            {
                yield return child;
            }
        }
    }
}

/// <summary>사용자에게 보이는 메뉴.</summary>
/// <param name="MenuId">메뉴 ID.</param>
/// <param name="Name">사용자 언어 메뉴명.</param>
/// <param name="MenuTypeCd">메뉴 유형.</param>
/// <param name="ProgramId">연결 프로그램.</param>
/// <param name="Url">열 주소.</param>
/// <param name="IconCd">아이콘 코드.</param>
/// <param name="NewWindow">새 창으로 열지.</param>
/// <param name="Children">하위 메뉴.</param>
public sealed record MenuItem(
    int MenuId,
    string Name,
    string MenuTypeCd,
    string? ProgramId,
    string? Url,
    string? IconCd,
    bool NewWindow,
    IReadOnlyList<MenuItem> Children);
