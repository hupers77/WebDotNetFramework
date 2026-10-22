using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.Web.Areas.Sys.Controllers;

/// <summary>
/// 역할별 권한 (표준 화면 6 권한 매트릭스). 왼쪽에서 역할을 고르면 오른쪽에 화면 × 기능(조회·등록·수정·삭제·내보내기·출력 등) 체크표가 나오고,
/// 체크 상태를 저장하면 그 역할을 가진 사용자의 버튼과 서버 권한이 바로 바뀐다.
/// </summary>
[Area("Sys")]
[Program(ProgramId)]
public sealed class RoleAuthController(IRoleAuthService roles, ICodeAdminService codes) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "SYS_ROLE_AUTH_MGT";

    /// <summary>화면.</summary>
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new RoleAuthIndexModel(
            [.. (await codes.GetCodesAsync("FUNC_CD", cancellationToken)).Codes.Items.Where(c => c.UseYn == "Y").Select(c => (c.Code, c.CodeNm))]));

    /// <summary>역할 목록.</summary>
    [HttpGet]
    public async Task<ApiResult<IReadOnlyList<RoleDto>>> Roles(CancellationToken cancellationToken) =>
        Ok(await roles.GetRolesAsync(cancellationToken));

    /// <summary>역할 한 개의 권한표.</summary>
    [HttpGet]
    public async Task<ApiResult<IReadOnlyList<RoleAuthProgram>>> List(
        [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")] string roleId,
        CancellationToken cancellationToken) =>
        Ok(await roles.GetProgramsAsync(roleId, cancellationToken));

    /// <summary>역할 한 개의 권한 저장. 바뀐 건수를 돌려준다.</summary>
    [HttpPost, Func(FuncCodes.Update)]
    public async Task<ApiResult<int>> Save([FromBody] RoleAuthSaveRequest request, CancellationToken cancellationToken) =>
        Ok(await roles.SaveAsync(request, cancellationToken));
}
