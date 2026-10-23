using Microsoft.AspNetCore.Mvc;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Rpt.Models;
using Portal.Web.Areas.Rpt.Services;

namespace Portal.Web.Areas.Rpt.Controllers;

/// <summary>
/// 화면 출력 공통 API (모든 화면의 [출력] 버튼이 쓴다, <c>fw.js</c>). 화면에 보이는 검색 조건과 표를 받아 PDF로 내려 준다.
/// 권한은 요청의 프로그램에 PRINT가 있는지로 확인한다(버튼도 같은 권한으로 그려진다).
/// </summary>
[Area("Rpt")]
[ProgramFromData]
public sealed class PrintController(IScreenPrintService prints) : PortalController
{
    /// <summary>요청 본문 상한 (표 5,000행 정도).</summary>
    private const long MaxBodyBytes = 10 * 1024 * 1024;

    /// <summary>현재 화면 PDF.</summary>
    [HttpPost, RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Pdf([FromBody] ScreenPrintRequest request)
    {
        var program = await prints.GetProgramAsync(request.ProgramId, HttpContext.RequestAborted) ?? throw new BizException(MessageKeys.NotFound);
        await DemandAnyFuncAsync(program.ProgramId, FuncCodes.Print);

        var content = await prints.PdfAsync(ControllerContext, program, request, HttpContext.RequestAborted);
        Response.Headers.CacheControl = "no-store";
        return File(content, "application/pdf", prints.FileName(program));
    }
}
