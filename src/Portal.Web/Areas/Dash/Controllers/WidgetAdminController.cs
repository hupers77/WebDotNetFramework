using Microsoft.AspNetCore.Mvc;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Dash.Models;
using Portal.Web.Areas.Dash.Services;

namespace Portal.Web.Areas.Dash.Controllers;

/// <summary>위젯 관리 (표준 화면 2 그리드 편집형, 수정만).</summary>
[Area("Dash")]
[Program(ProgramId)]
[Route("Dash/Widget")]
public sealed class WidgetAdminController(IWidgetAdminService widgets) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "DSH_WIDGET_MGT";

    /// <summary>화면.</summary>
    [HttpGet("")]
    public IActionResult Index() => View();

    /// <summary>위젯 목록.</summary>
    [HttpGet("List")]
    public async Task<ApiResult<PageResult<WidgetAdminDto>>> List(CancellationToken cancellationToken) =>
        Ok(await widgets.GetListAsync(cancellationToken));

    /// <summary>바뀐 위젯 저장.</summary>
    [HttpPost("Save"), Func(FuncCodes.Update)]
    public async Task<ApiResult<WidgetAdminSaveResult>> Save([FromBody] WidgetAdminSaveRequest request, CancellationToken cancellationToken)
    {
        await DemandRowFuncsAsync(request.Rows);
        return Ok(await widgets.SaveAsync(request, cancellationToken));
    }
}
