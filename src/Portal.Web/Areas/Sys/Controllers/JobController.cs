using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.Web.Areas.Sys.Controllers;

/// <summary>
/// 스케줄 관리 (표준 화면 3 마스터-디테일). 왼쪽 작업 목록에서 고르면 오른쪽에서 주기·제한 시간·사용 여부를 바꾸고,
/// 지금 실행을 요청하고, 최근 실행 이력을 본다. 작업은 Portal.Worker가 실행한다.
/// </summary>
[Area("Sys")]
[Program(ProgramId)]
public sealed class JobController(IJobAdminService jobs) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "SYS_JOB_MGT";

    /// <summary>화면.</summary>
    public IActionResult Index() => View();

    /// <summary>작업 목록.</summary>
    [HttpGet]
    public async Task<ApiResult<PageResult<JobDto>>> List([FromQuery] JobSearch search, CancellationToken cancellationToken) =>
        Ok(await jobs.GetListAsync(search, cancellationToken));

    /// <summary>작업 하나의 최근 실행 이력.</summary>
    [HttpGet]
    public async Task<ApiResult<PageResult<JobHistDto>>> History(
        [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")] string jobId,
        CancellationToken cancellationToken) =>
        Ok(await jobs.GetHistoryAsync(jobId, cancellationToken));

    /// <summary>작업 설정 저장.</summary>
    [HttpPost, Func(FuncCodes.Update)]
    public async Task<ApiResult> Save([FromBody] JobSaveRequest request, CancellationToken cancellationToken)
    {
        await jobs.SaveAsync(request, cancellationToken);
        return Ok();
    }

    /// <summary>지금 실행 요청.</summary>
    [HttpPost, Func(FuncCodes.Exec)]
    public async Task<ApiResult> Run([FromBody] JobRunRequest request, CancellationToken cancellationToken)
    {
        await jobs.RequestRunAsync(request, cancellationToken);
        return Ok();
    }
}
