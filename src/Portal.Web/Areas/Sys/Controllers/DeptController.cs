using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.Web.Areas.Sys.Controllers;

/// <summary>
/// 부서 관리 (표준 화면 5 트리-폼). 회사를 고르면 왼쪽에 부서 트리가 나오고, 부서를 고르면 오른쪽에서 이름·상위 부서·정렬 순서·사용 여부와
/// 부서 역할(하위 부서 상속 포함)을 고친다. 하위 부서나 소속 계정이 없는 잘못 만든 부서만 지운다.
/// </summary>
[Area("Sys")]
[Program(ProgramId)]
public sealed class DeptController(IDeptAdminService depts) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "SYS_DEPT_MGT";

    /// <summary>화면.</summary>
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new DeptIndexModel(await depts.GetCompaniesAsync(cancellationToken)));

    /// <summary>회사 한 곳의 부서 전부.</summary>
    [HttpGet]
    public async Task<ApiResult<IReadOnlyList<DeptDto>>> List([FromQuery] DeptSearch search, CancellationToken cancellationToken) =>
        Ok(await depts.GetListAsync(search, cancellationToken));

    /// <summary>부서 한 곳의 역할 선택지.</summary>
    [HttpGet]
    public async Task<ApiResult<IReadOnlyList<DeptRoleDto>>> Roles(
        [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(20, ErrorMessage = "MSG.INVALID_INPUT")] string deptId,
        CancellationToken cancellationToken) =>
        Ok(await depts.GetRolesAsync(deptId, cancellationToken));

    /// <summary>부서 등록.</summary>
    [HttpPost, Func(FuncCodes.Create)]
    public async Task<ApiResult<string>> Create([FromBody] DeptSaveRequest request, CancellationToken cancellationToken)
    {
        if (request.RowVer is not null)
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        return Ok(await depts.SaveAsync(request, cancellationToken));
    }

    /// <summary>부서 수정.</summary>
    [HttpPost, Func(FuncCodes.Update)]
    public async Task<ApiResult<string>> Save([FromBody] DeptSaveRequest request, CancellationToken cancellationToken)
    {
        if (request.RowVer is null || string.IsNullOrEmpty(request.DeptId))
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        return Ok(await depts.SaveAsync(request, cancellationToken));
    }

    /// <summary>부서 삭제.</summary>
    [HttpPost, Func(FuncCodes.Delete)]
    public async Task<ApiResult> Delete([FromBody] DeptDeleteRequest request, CancellationToken cancellationToken)
    {
        await depts.DeleteAsync(request, cancellationToken);
        return Ok();
    }
}
