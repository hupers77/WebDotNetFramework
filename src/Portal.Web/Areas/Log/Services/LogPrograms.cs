using Portal.Framework.Web.Navigation;

namespace Portal.Web.Areas.Log.Services;

/// <summary>로그 조회 화면의 프로그램 선택지와 화면 이름.</summary>
internal static class LogPrograms
{
    /// <summary>메뉴에 있는 프로그램 (ID, 화면 이름), 메뉴 순서.</summary>
    public static IReadOnlyList<(string Code, string Name)> List(MenuCatalog menus, string langCd)
    {
        var result = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Walk(MenuNode node)
        {
            if (node.ProgramId is { } programId && seen.Add(programId))
            {
                result.Add((programId, menus.ProgramTitle(programId, langCd)));
            }

            foreach (var child in node.Children)
            {
                Walk(child);
            }
        }

        foreach (var root in menus.Roots(MenuSets.Portal))
        {
            Walk(root);
        }

        return result;
    }
}
