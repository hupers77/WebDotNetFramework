using System.Text.Json;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Data;
using Portal.Web.Areas.Sys.Models;

namespace Portal.Web.Areas.Sys.Services;

/// <summary>부서 관리 (<c>SYS_DEPT_MGT</c>). 회사별 부서 트리, 부서 등록·수정(상위 부서·부서 역할 포함), 잘못 만든 부서 삭제.</summary>
/// <remarks>본사 부서는 3단계 AD 동기화가 만들고 고치며(AD 부서는 역할·정렬 순서만 여기서 바꾼다), 협력사와 AD 이전의 부서는 여기서 직접 만든다.</remarks>
public interface IDeptAdminService
{
    /// <summary>회사 한 곳의 부서 전부 (트리용).</summary>
    Task<IReadOnlyList<DeptDto>> GetListAsync(DeptSearch search, CancellationToken cancellationToken = default);

    /// <summary>부서 한 곳의 역할 선택지.</summary>
    Task<IReadOnlyList<DeptRoleDto>> GetRolesAsync(string deptId, CancellationToken cancellationToken = default);

    /// <summary>부서를 등록하거나 고치고 부서 ID를 돌려준다.</summary>
    Task<string> SaveAsync(DeptSaveRequest request, CancellationToken cancellationToken = default);

    /// <summary>하위 부서·소속 계정이 없는 부서를 지운다.</summary>
    Task DeleteAsync(DeptDeleteRequest request, CancellationToken cancellationToken = default);

    /// <summary>회사 선택지 (사용 중인 회사, 본사 먼저).</summary>
    Task<IReadOnlyList<(string Code, string Name)>> GetCompaniesAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class DeptAdminService(ISpExecutor sp) : IDeptAdminService
{
    internal const string ListSp = "FW.USP_DEPT_GET_LIST";
    internal const string RoleListSp = "FW.USP_DEPT_ROLE_GET_LIST";
    internal const string SaveSp = "FW.USP_DEPT_SAVE";
    internal const string DeleteSp = "FW.USP_DEPT_DEL";

    /// <inheritdoc />
    public Task<IReadOnlyList<DeptDto>> GetListAsync(DeptSearch search, CancellationToken cancellationToken = default) =>
        sp.QueryAsync<DeptDto>(ListSp, new { P_COMPANY_CD = search.CompanyCd, P_INCLUDE_UNUSED_YN = search.IncludeUnusedYn }, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<DeptRoleDto>> GetRolesAsync(string deptId, CancellationToken cancellationToken = default) =>
        sp.QueryAsync<DeptRoleDto>(RoleListSp, new { P_DEPT_ID = deptId }, cancellationToken);

    /// <inheritdoc />
    public async Task<string> SaveAsync(DeptSaveRequest request, CancellationToken cancellationToken = default)
    {
        var roles = request.Roles.Select(r => new { roleId = r.RoleId.Trim(), inheritYn = r.InheritYn }).ToList();
        if (roles.Exists(r => r.roleId.Length == 0) || roles.Select(r => r.roleId).Distinct(StringComparer.Ordinal).Count() != roles.Count)
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        var result = await sp.ExecuteAsync(
            SaveSp,
            new
            {
                P_DEPT_ID = request.RowVer is null ? null : request.DeptId,
                P_ROW_VER = request.RowVer,
                P_COMPANY_CD = request.CompanyCd,
                P_PARENT_DEPT_ID = string.IsNullOrWhiteSpace(request.ParentDeptId) ? null : request.ParentDeptId.Trim(),
                P_DEPT_NM = request.DeptNm.Trim(),
                P_SORT_NO = request.SortNo,
                P_USE_YN = request.UseYn,
                P_ROLES = JsonSerializer.Serialize(roles),
            },
            cancellationToken);
        return result.NewKey ?? request.DeptId ?? "";
    }

    /// <inheritdoc />
    public Task DeleteAsync(DeptDeleteRequest request, CancellationToken cancellationToken = default) =>
        sp.ExecuteAsync(DeleteSp, new { P_DEPT_ID = request.DeptId, P_ROW_VER = request.RowVer }, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<(string Code, string Name)>> GetCompaniesAsync(CancellationToken cancellationToken = default)
    {
        var page = await sp.QueryPageAsync<CompanyDto>(CompanyAdminService.ListSp, new { P_USE_YN = "Y", P_PAGE_NO = 1, P_PAGE_SIZE = 500 }, cancellationToken);
        return [.. page.Items.Select(c => (c.CompanyCd, c.CompanyNm))];
    }
}
