using Portal.Data;
using Portal.Web.Areas.Sys.Models;

namespace Portal.Web.Areas.Sys.Services;

/// <summary>역할 관리 (<c>SYS_ROLE_MGT</c>). 역할 목록, 역할 등록·수정(이름·설명·줄 수 있는 회사·데이터 범위·정렬·사용 여부), 잘못 만든 역할 삭제.</summary>
/// <remarks>역할이 쓸 수 있는 화면·버튼은 역할별 권한(<see cref="RoleAuthService"/>)에서 정한다. 시스템 역할·내 역할·SYS_ADMIN 보호 규칙은 SP가 강제한다.</remarks>
public interface IRoleAdminService
{
    /// <summary>역할 전부 (정렬 순서).</summary>
    Task<IReadOnlyList<RoleDto>> GetListAsync(CancellationToken cancellationToken = default);

    /// <summary>역할을 등록하거나 고치고 역할 ID를 돌려준다.</summary>
    Task<string> SaveAsync(RoleSaveRequest request, CancellationToken cancellationToken = default);

    /// <summary>부여되지 않은 역할을 기능 권한과 함께 지운다.</summary>
    Task DeleteAsync(RoleDeleteRequest request, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class RoleAdminService(ISpExecutor sp) : IRoleAdminService
{
    internal const string SaveSp = "FW.USP_ROLE_SAVE";
    internal const string DeleteSp = "FW.USP_ROLE_DEL";

    /// <inheritdoc />
    public Task<IReadOnlyList<RoleDto>> GetListAsync(CancellationToken cancellationToken = default) =>
        sp.QueryAsync<RoleDto>(RoleAuthService.RoleListSp, null, cancellationToken);

    /// <inheritdoc />
    public async Task<string> SaveAsync(RoleSaveRequest request, CancellationToken cancellationToken = default)
    {
        var result = await sp.ExecuteAsync(
            SaveSp,
            new
            {
                P_ROLE_ID = request.RoleId.Trim(),
                P_ROW_VER = request.RowVer,
                P_ROLE_NM = request.RoleNm.Trim(),
                P_DESCRIPTION = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                P_COMPANY_TYPE_CD = string.IsNullOrEmpty(request.CompanyTypeCd) ? null : request.CompanyTypeCd,
                P_DATA_SCOPE_CD = request.DataScopeCd,
                P_SORT_NO = request.SortNo,
                P_USE_YN = request.UseYn,
            },
            cancellationToken);
        return result.NewKey ?? request.RoleId;
    }

    /// <inheritdoc />
    public Task DeleteAsync(RoleDeleteRequest request, CancellationToken cancellationToken = default) =>
        sp.ExecuteAsync(DeleteSp, new { P_ROLE_ID = request.RoleId, P_ROW_VER = request.RowVer }, cancellationToken);
}
