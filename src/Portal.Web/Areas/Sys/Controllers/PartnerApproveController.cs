using Microsoft.AspNetCore.Mvc;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.Web.Areas.Sys.Controllers;

/// <summary>
/// 협력사 계정 승인 (표준 화면 3 마스터-디테일). 왼쪽 신청 목록에서 고르면 오른쪽에서 승인하거나 사유를 적어 반려한다.
/// 승인하면 신청한 ID·비밀번호로 협력사 사용자 계정이 생기고, 협력사 사용자 역할을 받는다.
/// </summary>
[Area("Sys")]
[Program(ProgramId)]
public sealed class PartnerApproveController(IPartnerApproveService joins, ICodeAdminService codes) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "SYS_PARTNER_APPROVE";

    /// <summary>신청 상태 공통코드 그룹.</summary>
    private const string StatGroup = "JOIN_STAT";

    /// <summary>화면.</summary>
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var stats = (await codes.GetCodesAsync(StatGroup, cancellationToken)).Codes.Items
            .Where(c => c.UseYn == "Y")
            .Select(c => (c.Code, c.CodeNm))
            .ToList();
        return View(new PartnerApproveIndexModel(stats));
    }

    /// <summary>신청 목록.</summary>
    [HttpGet]
    public async Task<ApiResult<PageResult<PartnerJoinDto>>> List([FromQuery] PartnerJoinSearch search, CancellationToken cancellationToken) =>
        Ok(await joins.GetListAsync(search, cancellationToken));

    /// <summary>승인. 만든 사용자 ID를 돌려준다.</summary>
    [HttpPost, Func(FuncCodes.Approve)]
    public async Task<ApiResult<string>> Approve([FromBody] PartnerJoinProcessRequest request, CancellationToken cancellationToken) =>
        Ok(await joins.ApproveAsync(request, cancellationToken));

    /// <summary>반려. 사유가 필요하다.</summary>
    [HttpPost, Func(FuncCodes.Approve)]
    public async Task<ApiResult> Reject([FromBody] PartnerJoinProcessRequest request, CancellationToken cancellationToken)
    {
        await joins.RejectAsync(request, cancellationToken);
        return Ok();
    }
}
