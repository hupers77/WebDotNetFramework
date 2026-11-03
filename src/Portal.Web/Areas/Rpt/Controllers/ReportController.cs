using Microsoft.AspNetCore.Mvc;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Rpt.Models;
using Portal.Web.Areas.Rpt.Services;

namespace Portal.Web.Areas.Rpt.Controllers;

/// <summary>
/// 레포트 관리 (표준 화면 2 그리드 편집형). 프로그램 하나를 레포트로 등록하면 조건 입력·미리보기·PDF·엑셀 화면(<c>/Rpt/Viewer/Index/{레포트 ID}</c>)이 생긴다.
/// 데이터 SP는 <c>스키마.USP_RPT_대상_GET_LIST</c> 형식만 받는다. 권한(조회·PDF·엑셀)은 레포트 ID(= 프로그램 ID)에 건다.
/// </summary>
[Area("Rpt")]
[Program(ProgramId)]
public sealed class ReportController(IReportAdminService reports) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "RPT_REPORT_MGT";

    /// <summary>화면.</summary>
    public IActionResult Index() => View();

    /// <summary>레포트 목록과 등록할 수 있는 프로그램.</summary>
    [HttpGet]
    public async Task<ApiResult<ReportAdminListResult>> List([FromQuery] ReportAdminSearch search, CancellationToken cancellationToken) =>
        Ok(await reports.GetListAsync(search, cancellationToken));

    /// <summary>바뀐 행 저장.</summary>
    [HttpPost, Func(FuncCodes.Update)]
    public async Task<ApiResult<ReportSaveResult>> Save([FromBody] ReportSaveRequest request, CancellationToken cancellationToken)
    {
        await DemandRowFuncsAsync(request.Rows);
        return Ok(await reports.SaveAsync(request, cancellationToken));
    }
}
