using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Log.Models;
using Portal.Web.Areas.Log.Services;

namespace Portal.Web.Areas.Log.Controllers;

/// <summary>
/// 데이터 변경 기록 (표준 화면 1 조회 + 상세). 기간(31일까지)·테이블·작업·대상 키·변경자·IP로 찾고, 한 건을 고르면 변경 전후 값을 나란히 보여 준다.
/// 한 건을 열면 개인정보 접속기록(조회)을 남기고, 엑셀은 사유를 받는다.
/// </summary>
[Area("Log")]
[Program(ProgramId)]
public sealed class AuditController(IAuditLogService logs) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = AuditLogService.ProgramId;

    /// <summary>화면.</summary>
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new AuditLogIndexModel(await logs.GetTablesAsync(cancellationToken)));

    /// <summary>변경 기록 목록 (변경 전후 값 없이).</summary>
    [HttpGet]
    public async Task<ApiResult<PageResult<AuditLogDto>>> List([FromQuery] AuditLogSearch search, CancellationToken cancellationToken) =>
        Ok(await logs.GetListAsync(search, cancellationToken));

    /// <summary>변경 기록 한 건 (변경 전후 값 포함).</summary>
    [HttpGet]
    public async Task<ApiResult<AuditLogDto>> Detail(
        [Range(1, long.MaxValue, ErrorMessage = "MSG.INVALID_INPUT")] long logId,
        [Required(ErrorMessage = "MSG.INVALID_INPUT")] DateTimeOffset? logDt,
        CancellationToken cancellationToken) =>
        Ok(await logs.GetAsync(logId, logDt!.Value.UtcDateTime, cancellationToken) ?? throw new BizException(MessageKeys.NotFound));

    /// <summary>검색 조건에 맞는 기록 엑셀 (변경 전후 값 포함, 사유 필수, 개인정보 접속기록).</summary>
    [HttpPost, Func(FuncCodes.Export)]
    public async Task<IActionResult> Export([FromBody] AuditLogExportRequest request, CancellationToken cancellationToken) =>
        Excel(await logs.ExportAsync(request.Search, request.Reason, cancellationToken), "audit-log.xlsx");
}
