using Microsoft.AspNetCore.Mvc;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Board.Models;
using Portal.Web.Areas.Board.Services;

namespace Portal.Web.Areas.Board.Controllers;

/// <summary>게시판 관리 (표준 화면 2 그리드 편집형, 수정만). 주소 <c>/Board/Admin</c>은 게시판 화면의 <c>Board/{boardCd}</c>보다 먼저 맞는다.</summary>
[Area("Board")]
[Program(ProgramId)]
[Route("Board/Admin")]
public sealed class BoardAdminController(IBoardAdminService boards) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "BRD_BOARD_MGT";

    /// <summary>화면.</summary>
    [HttpGet("")]
    public IActionResult Index() => View();

    /// <summary>게시판 목록.</summary>
    [HttpGet("List")]
    public async Task<ApiResult<PageResult<BoardAdminDto>>> List(CancellationToken cancellationToken) =>
        Ok(await boards.GetListAsync(cancellationToken));

    /// <summary>바뀐 게시판 저장.</summary>
    [HttpPost("Save"), Func(FuncCodes.Update)]
    public async Task<ApiResult<BoardAdminSaveResult>> Save([FromBody] BoardAdminSaveRequest request, CancellationToken cancellationToken)
    {
        await DemandRowFuncsAsync(request.Rows);
        return Ok(await boards.SaveAsync(request, cancellationToken));
    }
}
