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
/// 역할 관리 (표준 화면 3 마스터-디테일). 왼쪽 역할 목록에서 고르면 오른쪽에서 이름·설명·줄 수 있는 회사·데이터 범위·정렬 순서·사용 여부를 고친다.
/// 아무에게도 부여되지 않은 역할만 지우고, 화면·버튼 권한은 역할별 권한 화면(<c>SYS_ROLE_AUTH_MGT</c>)에서 정한다.
/// </summary>
[Area("Sys")]
[Program(ProgramId)]
public sealed class RoleController(IRoleAdminService roles, ICodeAdminService codes) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "SYS_ROLE_MGT";

    /// <summary>화면.</summary>
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        async Task<IReadOnlyList<(string Code, string Name)>> CodesAsync(string group) =>
            [.. (await codes.GetCodesAsync(group, cancellationToken)).Codes.Items.Where(c => c.UseYn == "Y").Select(c => (c.Code, c.CodeNm))];

        return View(new RoleIndexModel(await CodesAsync("COMPANY_TYPE"), await CodesAsync("DATA_SCOPE")));
    }

    /// <summary>역할 목록.</summary>
    [HttpGet]
    public async Task<ApiResult<IReadOnlyList<RoleDto>>> List(CancellationToken cancellationToken) =>
        Ok(await roles.GetListAsync(cancellationToken));

    /// <summary>역할 등록.</summary>
    [HttpPost, Func(FuncCodes.Create)]
    public async Task<ApiResult<string>> Create([FromBody] RoleSaveRequest request, CancellationToken cancellationToken)
    {
        if (request.RowVer is not null)
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        return Ok(await roles.SaveAsync(request, cancellationToken));
    }

    /// <summary>역할 수정.</summary>
    [HttpPost, Func(FuncCodes.Update)]
    public async Task<ApiResult<string>> Save([FromBody] RoleSaveRequest request, CancellationToken cancellationToken)
    {
        if (request.RowVer is null)
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        return Ok(await roles.SaveAsync(request, cancellationToken));
    }

    /// <summary>역할 삭제.</summary>
    [HttpPost, Func(FuncCodes.Delete)]
    public async Task<ApiResult> Delete([FromBody] RoleDeleteRequest request, CancellationToken cancellationToken)
    {
        await roles.DeleteAsync(request, cancellationToken);
        return Ok();
    }
}
