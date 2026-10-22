using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Infrastructure.Localization;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.Web.Areas.Sys.Controllers;

/// <summary>
/// 공통코드 관리 (표준 화면 3 마스터-디테일 + 2 그리드 편집형). 왼쪽 그룹 목록에서 고르면 오른쪽 그리드에서 코드를 일괄 편집한다.
/// 저장은 UPDATE 권한으로 열고, 추가 행은 CREATE, 삭제 행은 DELETE 권한을 추가로 확인한다.
/// </summary>
[Area("Sys")]
[Program(ProgramId)]
public sealed class CodeController(ICodeAdminService codes, LangResources langs) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "SYS_CODE_MGT";

    /// <summary>화면.</summary>
    public IActionResult Index() => View(new CodeIndexModel(langs.Languages));

    /// <summary>코드 그룹 목록.</summary>
    [HttpGet]
    public async Task<ApiResult<PageResult<CodeGroupDto>>> Groups([StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")] string? keyword, CancellationToken cancellationToken) =>
        Ok(await codes.GetGroupsAsync(keyword, cancellationToken));

    /// <summary>그룹의 코드 목록.</summary>
    [HttpGet]
    public async Task<ApiResult<CodeListDto>> List(
        [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")] string groupCd,
        CancellationToken cancellationToken) =>
        Ok(await codes.GetCodesAsync(groupCd, cancellationToken));

    /// <summary>그리드 일괄 저장. 처리 건수를 돌려준다.</summary>
    [HttpPost, Func(FuncCodes.Update)]
    public async Task<ApiResult<int>> Save([FromBody] CodeSaveRequest request, CancellationToken cancellationToken)
    {
        await DemandRowFuncsAsync(request.Rows);
        return Ok((await codes.SaveAsync(request, cancellationToken)).AffectedCnt);
    }

    /// <summary>그룹의 코드를 엑셀로 내려받는다.</summary>
    [HttpGet, Func(FuncCodes.Export)]
    public async Task<IActionResult> Export(
        [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(30, ErrorMessage = "MSG.INVALID_INPUT")] string groupCd,
        CancellationToken cancellationToken) =>
        Excel(await codes.ExportAsync(groupCd, cancellationToken), $"{groupCd}.xlsx");
}
