using Microsoft.AspNetCore.Mvc;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Log.Models;
using Portal.Web.Areas.Log.Services;
using Portal.Web.Areas.Sys.Services;

namespace Portal.Web.Areas.Log.Controllers;

/// <summary>
/// 로그인·접근 기록 (표준 화면 1 조회). 기간(31일까지)·사용자 ID·결과·화면·IP로 로그인 기록이나 화면·API 접근 기록을 찾는다.
/// 엑셀은 사유를 받아 개인정보 접속기록에 남긴다.
/// </summary>
[Area("Log")]
[Program(ProgramId)]
public sealed class AccessController(IAccessLogService logs, ICodeAdminService codes) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = AccessLogService.ProgramId;

    /// <summary>화면.</summary>
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new AccessLogIndexModel(
            [.. (await codes.GetCodesAsync("LOGIN_RESULT", cancellationToken)).Codes.Items.Where(c => c.UseYn == "Y").Select(c => (c.Code, c.CodeNm))],
            logs.GetPrograms()));

    /// <summary>로그인 기록.</summary>
    [HttpGet]
    public async Task<ApiResult<PageResult<LoginLogDto>>> Logins([FromQuery] AccessLogSearch search, CancellationToken cancellationToken) =>
        Ok(await logs.GetLoginsAsync(search, cancellationToken));

    /// <summary>화면·API 접근 기록.</summary>
    [HttpGet]
    public async Task<ApiResult<PageResult<AccessLogDto>>> Accesses([FromQuery] AccessLogSearch search, CancellationToken cancellationToken) =>
        Ok(await logs.GetAccessesAsync(search, cancellationToken));

    /// <summary>검색 조건에 맞는 기록 엑셀 (사유 필수, 개인정보 접속기록). 사유를 주소에 남기지 않으려고 POST로 받는다.</summary>
    [HttpPost, Func(FuncCodes.Export)]
    public async Task<IActionResult> Export([FromBody] AccessLogExportRequest request, CancellationToken cancellationToken) =>
        Excel(await logs.ExportAsync(request.Search, request.Reason, cancellationToken), request.Search.Kind == AccessLogKinds.Login ? "login-log.xlsx" : "access-log.xlsx");
}
