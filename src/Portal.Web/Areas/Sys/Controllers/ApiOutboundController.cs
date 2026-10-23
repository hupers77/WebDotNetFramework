using Microsoft.AspNetCore.Mvc;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.Web.Areas.Sys.Controllers;

/// <summary>송신 대상 관리 (표준 화면 2 그리드 편집형).</summary>
[Area("Sys")]
[Program(ProgramId)]
[Route("Sys/ApiOutbound")]
public sealed class ApiOutboundController(IApiOutboundAdminService targets) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "SYS_API_OUTBOUND_MGT";

    /// <summary>화면.</summary>
    [HttpGet("")]
    public IActionResult Index() => View();

    /// <summary>송신 대상 목록.</summary>
    [HttpGet("List")]
    public async Task<ApiResult<PageResult<ApiOutboundDto>>> List(CancellationToken cancellationToken) =>
        Ok(await targets.GetListAsync(cancellationToken));

    /// <summary>바뀐 행 저장.</summary>
    [HttpPost("Save"), Func(FuncCodes.Update)]
    public async Task<ApiResult<ApiOutboundSaveResult>> Save([FromBody] ApiOutboundSaveRequest request, CancellationToken cancellationToken)
    {
        await DemandRowFuncsAsync(request.Rows);
        return Ok(await targets.SaveAsync(request, cancellationToken));
    }
}
