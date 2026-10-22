using System.Globalization;
using System.Text.Json;
using Portal.Core.Context;
using Portal.Data;
using Portal.Framework.Web.Navigation;
using Portal.Web.Areas.Sys.Models;

namespace Portal.Web.Areas.Sys.Services;

/// <summary>사용자별 예외 권한 (<c>SYS_USER_AUTH_MGT</c>). 역할과 별개로 한 사람에게 기능(버튼)을 더 주거나(GRANT) 막는다(DENY).</summary>
/// <remarks>예외 권한은 버튼·서버 기능 권한만 바꾸고 데이터 범위는 역할을 따른다. 자기 예외 권한은 시스템 관리자만 고치고,
/// 시스템 관리자에게는 예외 권한을 두지 않는다(SP가 강제). 저장하면 권한 캐시가 바로 다시 계산된다.</remarks>
public interface IUserAuthService
{
    /// <summary>사용자 한 명의 예외 권한과 기능별 역할 허용 여부. 데이터 범위 밖이면 null.</summary>
    Task<UserAuthDetail?> GetAsync(string targetUserId, CancellationToken cancellationToken = default);

    /// <summary>예외 권한 전부를 저장하고 바뀐 건수(추가+삭제+변경)를 돌려준다.</summary>
    Task<int> SaveAsync(UserAuthSaveRequest request, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class UserAuthService(ISpExecutor sp, MenuCatalog menus, ICurrentUser currentUser) : IUserAuthService
{
    internal const string ListSp = "FW.USP_USER_AUTH_GET_LIST";
    internal const string SaveSp = "FW.USP_USER_AUTH_SAVE";

    /// <inheritdoc />
    public async Task<UserAuthDetail?> GetAsync(string targetUserId, CancellationToken cancellationToken = default)
    {
        var (target, rows) = await sp.QueryMultipleAsync(
            ListSp,
            new { P_TARGET_USER_ID = targetUserId },
            async r => (await r.ReadSingleAsync<UserAuthTarget>(cancellationToken), await r.ReadAsync<UserAuthRow>(cancellationToken)),
            cancellationToken);
        if (target is null)
        {
            return null;
        }

        var programs = rows
            .GroupBy(r => r.ProgramId, StringComparer.Ordinal)
            .Select(g =>
            {
                var first = g.First();
                var title = menus.ProgramTitle(g.Key, currentUser.LangCd);
                return new UserAuthProgram(
                    g.Key,
                    title == g.Key ? first.ProgramNm : title,
                    first.GrantableYn == "Y",
                    [.. g.Select(r => new UserAuthFuncState(r.FuncCd, r.RoleYn == "Y", r.GrantType?.Trim(), r.EndDt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))]);
            })
            .OrderBy(p => p.ProgramNm, StringComparer.CurrentCulture)
            .ToList();
        return new UserAuthDetail(target, programs);
    }

    /// <inheritdoc />
    public async Task<int> SaveAsync(UserAuthSaveRequest request, CancellationToken cancellationToken = default)
    {
        var rows = request.Rows.Select(r => new
        {
            programId = r.ProgramId.Trim(),
            funcCd = r.FuncCd.Trim(),
            grantType = r.GrantType,
            endDt = r.EndDt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        });
        return (await sp.ExecuteAsync(
            SaveSp,
            new { P_TARGET_USER_ID = request.TargetUserId, P_VERSION = request.Version, P_ROWS = JsonSerializer.Serialize(rows) },
            cancellationToken)).AffectedCnt;
    }
}
