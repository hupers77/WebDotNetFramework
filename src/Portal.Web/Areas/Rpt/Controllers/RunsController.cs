using Microsoft.AspNetCore.Mvc;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Logging;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Rpt.Models;
using Portal.Web.Areas.Rpt.Services;

namespace Portal.Web.Areas.Rpt.Controllers;

/// <summary>
/// 내 레포트 (<c>RPT_RUNS</c>, 내 정보 메뉴): 백그라운드로 요청한 레포트의 진행과 결과 파일. 모든 역할이 자기 요청만 본다(SP가 현재 사용자로 거른다).
/// 목록은 진행 중이면 화면이 몇 초마다 다시 부르므로 세션을 연장하지 않고(<c>X-Portal-Background</c>) 접속기록도 남기지 않는다.
/// 내려받을 때는 그 레포트의 PRINT(PDF)·EXPORT(엑셀)를 한 번 더 확인하고, 접속기록에는 내 레포트(<c>RPT_RUNS</c>)와 받은 형식의 기능(PRINT·EXPORT)이 남는다.
/// </summary>
[Area("Rpt")]
[Program(ProgramId)]
public sealed class RunsController(IReportRunService runs) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "RPT_RUNS";

    /// <summary>완성된 파일을 보관하는 일수 (<c>USP_REPORT_RUN_END_EXEC</c>의 7일과 같다).</summary>
    public const int KeepDays = 7;

    /// <summary>화면.</summary>
    public IActionResult Index() => View(new ReportRunsViewModel(KeepDays));

    /// <summary>내 요청 목록.</summary>
    [HttpGet, SkipAccessLog]
    public async Task<ApiResult<IReadOnlyList<ReportRunItem>>> List(CancellationToken cancellationToken) =>
        Ok(await runs.GetListAsync(cancellationToken));

    /// <summary>완성된 파일 내려받기. 요청 번호를 주소에 남기지 않으려고 POST로 받는다.</summary>
    [HttpPost]
    public async Task<IActionResult> Download([FromBody] ReportRunIdRequest request, CancellationToken cancellationToken)
    {
        var file = await runs.GetAsync(request.RunId, cancellationToken);
        if (file is not { StatusCd: "DONE" } || ReportFormats.FuncOf(file.FormatCd) is not { } func)
        {
            throw new BizException(MessageKeys.NotFound);
        }

        await DemandAnyFuncAsync(file.ReportId, func);
        Response.Headers.CacheControl = "no-store";
        var contentType = file.FormatCd == ReportFormats.Pdf ? "application/pdf" : Portal.Infrastructure.Excel.ExcelWriter.ContentType;
        return File(runs.Open(file), contentType, file.FileNm);
    }

    /// <summary>내 요청 삭제 (목록에서 빠지고 파일은 정리가 지운다).</summary>
    [HttpPost, Func(FuncCodes.Delete)]
    public async Task<ApiResult> Delete([FromBody] ReportRunIdRequest request, CancellationToken cancellationToken)
    {
        await runs.DeleteAsync(request.RunId, cancellationToken);
        return Ok();
    }
}
