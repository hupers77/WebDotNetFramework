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
/// 사용자별 예외 권한 (표준 화면 3 마스터-디테일). 왼쪽에서 사용자를 고르면 오른쪽에 그 사람의 예외 권한 목록이 나온다.
/// 역할과 별개로 기능(조회·등록·수정·삭제·내보내기·출력 등)을 기간을 정해 더 주거나(추가 허용) 막는다(차단). 차단이 역할 허용보다 우선한다.
/// </summary>
[Area("Sys")]
[Program(ProgramId)]
public sealed class UserAuthController(IUserAuthService auths, IUserAdminService users, ICodeAdminService codes) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "SYS_USER_AUTH_MGT";

    /// <summary>화면.</summary>
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new UserAuthIndexModel(
            [.. (await codes.GetCodesAsync("FUNC_CD", cancellationToken)).Codes.Items.Where(c => c.UseYn == "Y").Select(c => (c.Code, c.CodeNm))],
            await users.GetCompaniesAsync(cancellationToken)));

    /// <summary>사용자 목록 (이 화면의 데이터 범위 안).</summary>
    [HttpGet]
    public async Task<ApiResult<PageResult<UserAdminDto>>> Users([FromQuery] UserSearch search, CancellationToken cancellationToken) =>
        Ok(await users.GetListAsync(ProgramId, search, cancellationToken));

    /// <summary>사용자 한 명의 예외 권한과 기능별 역할 허용 여부.</summary>
    [HttpGet]
    public async Task<ApiResult<UserAuthDetail>> List(
        [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")] string userId,
        CancellationToken cancellationToken) =>
        Ok(await auths.GetAsync(userId, cancellationToken) ?? throw new BizException(MessageKeys.NotFound));

    /// <summary>사용자 한 명의 예외 권한 전부 저장. 바뀐 건수를 돌려준다.</summary>
    [HttpPost, Func(FuncCodes.Update)]
    public async Task<ApiResult<int>> Save([FromBody] UserAuthSaveRequest request, CancellationToken cancellationToken) =>
        Ok(await auths.SaveAsync(request, cancellationToken));
}
