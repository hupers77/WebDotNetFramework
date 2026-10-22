using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.Web.Areas.Ptn.Controllers;

/// <summary>
/// 협력사 사용자 관리 (협력사 관리자 위임). 본사 사용자 관리와 같은 화면·서비스를 쓰되 프로그램이 <c>PTN_USER_MGT</c>라서
/// SP가 데이터 범위로 자기 회사 사용자만 보여 주고 고치게 한다. 줄 수 있는 역할은 협력사 역할뿐이다.
/// </summary>
[Area("Ptn")]
[Program(ProgramId)]
public sealed class UserController(IUserAdminService users, ICodeAdminService codes) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "PTN_USER_MGT";

    private const string ViewPath = "~/Areas/Sys/Views/User/Index.cshtml";

    /// <summary>화면.</summary>
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var stats = (await codes.GetCodesAsync("USER_STAT", cancellationToken)).Codes.Items.Where(c => c.UseYn == "Y").Select(c => (c.Code, c.CodeNm));
        return View(ViewPath, new UserAdminIndexModel(ProgramId, true, [], [], [.. stats]));
    }

    /// <summary>자기 회사 사용자 목록.</summary>
    [HttpGet]
    public async Task<ApiResult<PageResult<UserAdminDto>>> List([FromQuery] UserSearch search, CancellationToken cancellationToken) =>
        Ok(await users.GetListAsync(ProgramId, search, cancellationToken));

    /// <summary>자기 회사 사용자 엑셀 (사유 필수, 개인정보 접속기록). 버튼은 역할별 권한의 EXPORT로 연다.</summary>
    [HttpPost, Func(FuncCodes.Export)]
    public async Task<IActionResult> Export([FromBody] UserExportRequest request, CancellationToken cancellationToken) =>
        Excel(await users.ExportAsync(ProgramId, request.Search, request.Reason, cancellationToken), "users.xlsx");

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
