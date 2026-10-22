using System.Text.Json;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Data;
using Portal.Web.Areas.Sys.Models;

namespace Portal.Web.Areas.Sys.Services;

/// <summary>사용자 관리 (<c>SYS_USER_MGT</c>). 목록, 이름·상태·사용 기한·직접 부여 역할 저장, 잠금 해제, 2단계 인증 초기화.</summary>
/// <remarks>본사 사용자는 3단계 AD 동기화로, 협력사 사용자는 가입 승인으로 만들어지므로 여기서는 새로 만들거나 지우지 않는다(퇴사·계약 종료는 상태로).</remarks>
public interface IUserAdminService
{
    /// <summary>사용자 목록 (한 쪽 <see cref="UserAdminService.PageSize"/>명).</summary>
    Task<PageResult<UserAdminDto>> GetListAsync(UserSearch search, CancellationToken cancellationToken = default);

    /// <summary>사용자 한 명의 역할 선택지.</summary>
    Task<IReadOnlyList<UserRoleDto>> GetRolesAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>이름·상태·사용 기한·역할 저장.</summary>
    Task SaveAsync(UserSaveRequest request, CancellationToken cancellationToken = default);

    /// <summary>잠금 해제 또는 2단계 인증 초기화. 바꾼 것이 없으면 false.</summary>
    Task<bool> ResetAsync(string userId, string actionCd, CancellationToken cancellationToken = default);

    /// <summary>검색용 회사 목록 (코드, 이름).</summary>
    Task<IReadOnlyList<(string Code, string Name)>> GetCompaniesAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class UserAdminService(ISpExecutor sp) : IUserAdminService
{
    /// <summary>한 쪽에 보이는 사용자 수.</summary>
    public const int PageSize = 50;

    internal const string ListSp = "FW.USP_USER_GET_LIST";
    internal const string RoleListSp = "FW.USP_USER_ROLE_GET_LIST";
    internal const string SaveSp = "FW.USP_USER_SAVE";
    internal const string ResetSp = "FW.USP_USER_ACCOUNT_RESET_EXEC";

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <inheritdoc />
    public Task<PageResult<UserAdminDto>> GetListAsync(UserSearch search, CancellationToken cancellationToken = default) =>
        sp.QueryPageAsync<UserAdminDto>(
            ListSp,
            new
            {
                P_KEYWORD = Blank(search.Keyword),
                P_COMPANY_CD = Blank(search.CompanyCd),
                P_USER_TYPE_CD = Blank(search.UserTypeCd),
                P_USER_STAT_CD = Blank(search.UserStatCd),
                P_LOCK_YN = Blank(search.LockYn),
                P_PAGE_NO = search.PageNo,
                P_PAGE_SIZE = PageSize,
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<UserRoleDto>> GetRolesAsync(string userId, CancellationToken cancellationToken = default) =>
        sp.QueryAsync<UserRoleDto>(RoleListSp, new { P_TARGET_USER_ID = userId }, cancellationToken);

    /// <inheritdoc />
    public async Task SaveAsync(UserSaveRequest request, CancellationToken cancellationToken = default)
    {
        var roles = request.RoleIds.Select(r => r?.Trim() ?? "").Distinct(StringComparer.Ordinal).ToList();
        if (roles.Exists(r => r.Length is 0 or > 30))
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        await sp.ExecuteAsync(
            SaveSp,
            new
            {
                P_TARGET_USER_ID = request.UserId,
                P_ROW_VER = request.RowVer,
                P_USER_NM = request.UserNm.Trim(),
                P_USER_STAT_CD = request.UserStatCd,
                P_ACCOUNT_END_DT = request.AccountEndDt,
                P_ROLE_IDS = JsonSerializer.Serialize(roles),
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ResetAsync(string userId, string actionCd, CancellationToken cancellationToken = default) =>
        (await sp.ExecuteAsync(ResetSp, new { P_TARGET_USER_ID = userId, P_ACTION_CD = actionCd }, cancellationToken)).AffectedCnt > 0;

    /// <inheritdoc />
    public async Task<IReadOnlyList<(string Code, string Name)>> GetCompaniesAsync(CancellationToken cancellationToken = default)
    {
        var page = await sp.QueryPageAsync<CompanyDto>(CompanyAdminService.ListSp, new { P_PAGE_NO = 1, P_PAGE_SIZE = 500 }, cancellationToken);
        return [.. page.Items.Select(c => (c.CompanyCd, c.CompanyNm))];
    }
}
