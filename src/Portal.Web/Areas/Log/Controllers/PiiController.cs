using Microsoft.AspNetCore.Mvc;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Log.Models;
using Portal.Web.Areas.Log.Services;

namespace Portal.Web.Areas.Log.Controllers;

/// <summary>
/// 개인정보 접속기록 (표준 화면 1 조회). 기간(31일까지)·사용자 ID·화면·작업·IP로 개인정보 조회·다운로드·마스킹 해제 기록을 찾는다.
/// 엑셀은 사유를 받아 개인정보 접속기록에 남긴다.
/// </summary>
[Area("Log")]
[Program(ProgramId)]
public sealed class PiiController(IPiiLogService logs) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = PiiLogService.ProgramId;

    /// <summary>화면.</summary>
    public IActionResult Index() => View(new PiiLogIndexModel(logs.GetPrograms()));

    /// <summary>개인정보 접속기록 목록.</summary>
    [HttpGet]
    public async Task<ApiResult<PageResult<PiiLogDto>>> List([FromQuery] PiiLogSearch search, CancellationToken cancellationToken) =>
        Ok(await logs.GetListAsync(search, cancellationToken));

    /// <summary>검색 조건에 맞는 기록 엑셀 (사유 필수, 개인정보 접속기록). 사유를 주소에 남기지 않으려고 POST로 받는다.</summary>
    [HttpPost, Func(FuncCodes.Export)]
    public async Task<IActionResult> Export([FromBody] PiiLogExportRequest request, CancellationToken cancellationToken) =>
        Excel(await logs.ExportAsync(request.Search, request.Reason, cancellationToken), "pii-access-log.xlsx");
}
