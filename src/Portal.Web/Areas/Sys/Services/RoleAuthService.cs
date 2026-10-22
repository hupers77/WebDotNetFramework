using System.Text.Json;
using Portal.Core.Context;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Localization;
using Portal.Data;
using Portal.Framework.Web.Navigation;
using Portal.Web.Areas.Sys.Models;

namespace Portal.Web.Areas.Sys.Services;

/// <summary>역할별 권한 (<c>SYS_ROLE_AUTH_MGT</c>). 역할을 고르면 화면(프로그램) × 기능(버튼) 권한표를 보여 주고 체크 상태를 통째로 저장한다.</summary>
/// <remarks>시스템 관리자 역할은 코드에서 모든 권한을 가지므로 고치지 않고, 자기가 가진 역할은 시스템 관리자만 고친다(SP가 강제).
/// 저장하면 권한 캐시가 바로 다시 계산된다.</remarks>
public interface IRoleAuthService
{
    /// <summary>역할 목록.</summary>
    Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>역할 한 개의 권한표 (메뉴 순서, 최상위 메뉴로 묶음).</summary>
    Task<IReadOnlyList<RoleAuthProgram>> GetProgramsAsync(string roleId, CancellationToken cancellationToken = default);

    /// <summary>체크된 기능 전부를 저장하고 바뀐 건수(추가+삭제)를 돌려준다.</summary>
    Task<int> SaveAsync(RoleAuthSaveRequest request, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class RoleAuthService(ISpExecutor sp, MenuCatalog menus, ICurrentUser currentUser, IPortalLocalizer localizer) : IRoleAuthService
{
    internal const string RoleListSp = "FW.USP_ROLE_GET_LIST";
    internal const string ListSp = "FW.USP_ROLE_AUTH_GET_LIST";
    internal const string SaveSp = "FW.USP_ROLE_AUTH_SAVE";

    /// <inheritdoc />
    public Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default) =>
        sp.QueryAsync<RoleDto>(RoleListSp, null, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleAuthProgram>> GetProgramsAsync(string roleId, CancellationToken cancellationToken = default)
    {
        var rows = await sp.QueryAsync<RoleAuthRow>(ListSp, new { P_ROLE_ID = roleId }, cancellationToken);
        var order = MenuOrder();
        var others = localizer["SYS.ROLE_AUTH.NO_MENU"];
        return
        [
            .. rows
                .GroupBy(r => r.ProgramId, StringComparer.Ordinal)
                .Select(g =>
                {
                    var first = g.First();
                    var found = order.TryGetValue(g.Key, out var place);
                    var title = menus.ProgramTitle(g.Key, currentUser.LangCd);
                    return (
                        Index: found ? place.Index : int.MaxValue,
                        Program: new RoleAuthProgram(
                            found ? place.Group : others,
                            g.Key,
                            title == g.Key ? first.ProgramNm : title,
                            first.OpenScopeCd,
                            first.GrantableYn == "Y",
                            [.. g.Select(r => new RoleAuthFuncState(r.FuncCd, r.GrantedYn == "Y"))]));
                })
                .OrderBy(p => p.Index)
                .ThenBy(p => p.Program.ProgramId, StringComparer.Ordinal)
                .Select(p => p.Program),
        ];
    }

    /// <inheritdoc />
    public async Task<int> SaveAsync(RoleAuthSaveRequest request, CancellationToken cancellationToken = default)
    {
        var funcs = request.Funcs
            .Select(f => new { programId = f.ProgramId.Trim(), funcCd = f.FuncCd.Trim() })
            .Distinct()
            .ToList();
        if (funcs.Exists(f => f.programId.Length == 0 || f.funcCd.Length == 0))
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        return (await sp.ExecuteAsync(
            SaveSp,
            new { P_ROLE_ID = request.RoleId, P_ROW_VER = request.RowVer, P_FUNCS = JsonSerializer.Serialize(funcs) },
            cancellationToken)).AffectedCnt;
    }

    // 프로그램 → (메뉴 트리 순서, 최상위 메뉴명). 한 프로그램이 여러 메뉴에 있으면 처음 나온 곳.
    private Dictionary<string, (int Index, string Group)> MenuOrder()
    {
        var result = new Dictionary<string, (int, string)>(StringComparer.Ordinal);
        var index = 0;

        void Walk(MenuNode node, string group)
        {
            if (node.ProgramId is { } programId)
            {
                result.TryAdd(programId, (index++, group));
            }

            foreach (var child in node.Children)
            {
                Walk(child, group);
            }
        }

        foreach (var root in menus.Roots(MenuSets.Portal))
        {
            Walk(root, Name(root));
        }

        return result;
    }

    private string Name(MenuNode node) =>
        node.Names.GetValueOrDefault(currentUser.LangCd) ?? node.Names.GetValueOrDefault(Portal.Infrastructure.Localization.LangResources.DefaultLang) ?? "";
}
