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
/// 사용자 관리 (표준 화면 3 마스터-디테일). 왼쪽 사용자 목록에서 고르면 오른쪽에서 이름·상태·사용 기한·역할을 고치고,
/// 비밀번호 실패 잠금을 풀거나 2단계 인증 등록을 초기화한다. 협력사 사용자에게는 본사 역할을 줄 수 없다.
/// </summary>
[Area("Sys")]
[Program(ProgramId)]
public sealed class UserController(IUserAdminService users, ICodeAdminService codes) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "SYS_USER_MGT";

    /// <summary>화면.</summary>
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        async Task<IReadOnlyList<(string, string)>> CodesAsync(string group) =>
            [.. (await codes.GetCodesAsync(group, cancellationToken)).Codes.Items.Where(c => c.UseYn == "Y").Select(c => (c.Code, c.CodeNm))];

        return View(new UserAdminIndexModel(
            ProgramId,
            false,
            await users.GetCompaniesAsync(cancellationToken),
            await CodesAsync("USER_TYPE"),
            await CodesAsync("USER_STAT")));
    }

    /// <summary>사용자 목록.</summary>
    [HttpGet]
    public async Task<ApiResult<PageResult<UserAdminDto>>> List([FromQuery] UserSearch search, CancellationToken cancellationToken) =>
        Ok(await users.GetListAsync(ProgramId, search, cancellationToken));

    /// <summary>사용자 한 명의 역할 선택지.</summary>
    [HttpGet]
    public async Task<ApiResult<IReadOnlyList<UserRoleDto>>> Roles(
        [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")] string userId,
        CancellationToken cancellationToken) =>
        Ok(await users.GetRolesAsync(ProgramId, userId, cancellationToken));

    /// <summary>사용자 한 명에게 지정할 수 있는 부서.</summary>
    [HttpGet]
    public async Task<ApiResult<IReadOnlyList<UserDeptDto>>> Depts(
        [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")] string userId,
        CancellationToken cancellationToken) =>
        Ok(await users.GetDeptsAsync(ProgramId, userId, cancellationToken));

    /// <summary>이름·상태·사용 기한·부서·역할 저장.</summary>
    [HttpPost, Func(FuncCodes.Update)]
    public async Task<ApiResult> Save([FromBody] UserSaveRequest request, CancellationToken cancellationToken)
    {
        await users.SaveAsync(ProgramId, request, cancellationToken);
        return Ok();
    }

    /// <summary>비밀번호 실패 잠금 해제.</summary>
    [HttpPost, Func(FuncCodes.Update)]
    public async Task<ApiResult<bool>> Unlock([FromBody] UserResetRequest request, CancellationToken cancellationToken) =>
        Ok(await users.ResetAsync(ProgramId, request.UserId, UserResetActions.Unlock, cancellationToken));

    /// <summary>2단계 인증 등록 초기화 (다음 로그인에서 인증 앱을 다시 등록).</summary>
    [HttpPost, Func(FuncCodes.Update)]
    public async Task<ApiResult<bool>> ResetMfa([FromBody] UserResetRequest request, CancellationToken cancellationToken) =>
        Ok(await users.ResetAsync(ProgramId, request.UserId, UserResetActions.MfaReset, cancellationToken));
}
