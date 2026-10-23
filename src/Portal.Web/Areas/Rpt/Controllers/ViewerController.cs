using Microsoft.AspNetCore.Mvc;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Rpt.Models;
using Portal.Web.Areas.Rpt.Services;

namespace Portal.Web.Areas.Rpt.Controllers;

/// <summary>
/// 레포트 화면 (<c>/Rpt/Viewer/Index/{레포트 ID}</c>, 메뉴에 이 주소로 등록). <c>TB_REPORT</c>에 등록한 레포트마다 같은 화면을 쓴다:
/// 조건 입력, 미리보기, PDF, 엑셀. 권한은 레포트 ID(= 프로그램 ID)로 Action이 확인한다: 화면·미리보기 READ, PDF PRINT, 엑셀 EXPORT.
/// </summary>
[Area("Rpt")]
[ProgramFromData]
public sealed class ViewerController(IReportService reports) : PortalController
{
    /// <summary>조건 입력 화면. 없거나 사용 안 함인 레포트는 404.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(string id)
    {
        if (await reports.GetAsync(id, HttpContext.RequestAborted) is not { } definition)
        {
            return NotFound();
        }

        await DemandAnyFuncAsync(definition.ReportId, FuncCodes.Read);
        return View(reports.Viewer(definition));
    }

    /// <summary>미리보기 HTML. 조건은 쿼리 문자열(<c>P_MONTH=2026-09</c>)로 받는다(접근 기록에는 쿼리를 남기지 않는다).</summary>
    [HttpGet]
    public async Task<ApiResult<ReportPreview>> Preview(string id)
    {
        var definition = await RequireAsync(id, FuncCodes.Read);
        var input = Request.Query
            .Where(q => q.Key.StartsWith("P_", StringComparison.Ordinal))
            .ToDictionary(q => q.Key, q => (string?)q.Value.ToString(), StringComparer.Ordinal);
        Response.Headers.CacheControl = "no-store";
        return Ok(await reports.PreviewAsync(ControllerContext, definition, input, HttpContext.RequestAborted));
    }

    /// <summary>PDF (PRINT). 사유를 주소에 남기지 않으려고 POST로 받는다.</summary>
    [HttpPost]
    public async Task<IActionResult> Pdf(string id, [FromBody] ReportRunRequest request)
    {
        var definition = await RequireAsync(id, FuncCodes.Print);
        var content = await reports.PdfAsync(ControllerContext, definition, request, HttpContext.RequestAborted);
        Response.Headers.CacheControl = "no-store";
        return File(content, "application/pdf", reports.FileName(definition, "pdf"));
    }

    /// <summary>엑셀 (EXPORT).</summary>
    [HttpPost]
    public async Task<IActionResult> Excel(string id, [FromBody] ReportRunRequest request)
    {
        var definition = await RequireAsync(id, FuncCodes.Export);
        return Excel(await reports.ExcelAsync(definition, request, HttpContext.RequestAborted), reports.FileName(definition, "xlsx"));
    }

    private async Task<ReportDefinition> RequireAsync(string id, string func)
    {
        var definition = await reports.GetAsync(id, HttpContext.RequestAborted) ?? throw new BizException(MessageKeys.NotFound);
        await DemandAnyFuncAsync(definition.ReportId, func);
        return definition;
    }
}
