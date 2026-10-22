using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.Web.Areas.Sys.Controllers;

/// <summary>
/// 프로그램 관리 (표준 화면 3 마스터-디테일). 왼쪽 프로그램 목록에서 고르면 오른쪽에서 관리용 이름·개인정보 처리 여부·사용 여부와
/// 이 화면이 쓰는 기능(조회·등록·수정·삭제·내보내기·출력 등)을 고친다. 고른 기능이 역할별 권한 화면의 체크 칸이 된다.
/// </summary>
[Area("Sys")]
[Program(ProgramId)]
public sealed class ProgramController(IProgramAdminService programs, ICodeAdminService codes) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "SYS_PROGRAM_MGT";

    /// <summary>화면.</summary>
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new ProgramIndexModel(
            [.. (await codes.GetCodesAsync("FUNC_CD", cancellationToken)).Codes.Items.Where(c => c.UseYn == "Y").Select(c => (c.Code, c.CodeNm))]));

    /// <summary>프로그램 목록.</summary>
    [HttpGet]
    public async Task<ApiResult<IReadOnlyList<ProgramDto>>> List(CancellationToken cancellationToken) =>
        Ok(await programs.GetListAsync(cancellationToken));

    /// <summary>프로그램 한 개의 기능 선택지와 사용 현황.</summary>
    [HttpGet]
    public async Task<ApiResult<IReadOnlyList<ProgramFuncDto>>> Funcs(
        [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")] string programId,
        CancellationToken cancellationToken) =>
        Ok(await programs.GetFuncsAsync(programId, cancellationToken));

    /// <summary>프로그램 등록.</summary>
    [HttpPost, Func(FuncCodes.Create)]
    public async Task<ApiResult<string>> Create([FromBody] ProgramSaveRequest request, CancellationToken cancellationToken)
    {
        if (request.RowVer is not null)
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        return Ok(await programs.SaveAsync(request, cancellationToken));
    }

    /// <summary>프로그램 수정.</summary>
    [HttpPost, Func(FuncCodes.Update)]
    public async Task<ApiResult<string>> Save([FromBody] ProgramSaveRequest request, CancellationToken cancellationToken)
    {
        if (request.RowVer is null)
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        return Ok(await programs.SaveAsync(request, cancellationToken));
    }

    /// <summary>프로그램 삭제.</summary>
    [HttpPost, Func(FuncCodes.Delete)]
    public async Task<ApiResult> Delete([FromBody] ProgramDeleteRequest request, CancellationToken cancellationToken)
    {
        await programs.DeleteAsync(request, cancellationToken);
        return Ok();
    }
}
