using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Board.Models;
using Portal.Web.Areas.Board.Services;

namespace Portal.Web.Areas.Board.Controllers;

/// <summary>
/// 게시판 화면 (<c>/Board/{게시판 코드}</c>, 메뉴에 이 주소로 등록). <c>TB_BOARD</c>에 등록한 게시판마다 같은 화면을 쓴다:
/// 목록(검색·쪽 넘김), 글 보기(첨부·댓글·조회자), 글쓰기·수정·답글, 삭제.
/// 권한은 게시판의 프로그램(<c>TB_BOARD.PROGRAM_ID</c>)으로 Action이 확인한다: 읽기 READ, 글쓰기·답글·댓글 CREATE, 남의 글 수정·상단 고정·공개 대상 UPDATE, 남의 글·댓글 삭제 DELETE.
/// 본인 글은 CREATE만 있어도 고치고 지울 수 있다. 글 단위 규칙(게시기간, 공개 대상 회사, 비밀글)은 SP가 지킨다.
/// </summary>
[Area("Board")]
[ProgramFromData]
[Route("Board/{boardCd}")]
public sealed partial class BoardController(IBoardService boards, CurrentPermissions permissions) : PortalController
{
    [GeneratedRegex("^[A-Za-z0-9_]{1,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex BoardCodePattern();

    /// <summary>목록 화면.</summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(string boardCd)
    {
        if (await FindAsync(boardCd) is not { } board)
        {
            return NotFound();
        }

        var granted = await DemandAsync(board, FuncCodes.Read);
        return View(new BoardIndexModel(board, granted.Can(board.ProgramId, FuncCodes.Create)));
    }

    /// <summary>글 목록.</summary>
    [HttpGet("List")]
    public async Task<ApiResult<PageResult<PostRowDto>>> List(string boardCd, [FromQuery] PostSearch search)
    {
        var board = await RequireAsync(boardCd);
        var granted = await DemandAsync(board, FuncCodes.Read);
        return Ok(await boards.GetListAsync(board.BoardCd, search, granted.Can(board.ProgramId, FuncCodes.Update), HttpContext.RequestAborted));
    }

    /// <summary>글 보기 화면. 본문을 열면 조회 수와 조회자 기록이 남는다. 볼 수 없는 글은 없는 글과 같이 404다.</summary>
    [HttpGet("View/{id:long}")]
    public async Task<IActionResult> View(string boardCd, long id)
    {
        if (await FindAsync(boardCd) is not { } board)
        {
            return NotFound();
        }

        var granted = await DemandAsync(board, FuncCodes.Read);
        var manager = granted.Can(board.ProgramId, FuncCodes.Update);
        PostDetail detail;
        try
        {
            detail = await boards.GetPostAsync(id, countView: true, manager, HttpContext.RequestAborted);
        }
        catch (BizException ex) when (ex.MessageKey == MessageKeys.NotFound)
        {
            return NotFound();
        }

        if (!string.Equals(detail.Post.BoardCd, board.BoardCd, StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        var mine = detail.Post.MineYn == "Y";
        var canCreate = granted.Can(board.ProgramId, FuncCodes.Create);
        return View(new BoardViewModel(
            board,
            detail,
            CanReply: board.ReplyYn == "Y" && canCreate && detail.Post.Depth < 3,
            CanEdit: manager || (mine && canCreate),
            CanDelete: granted.Can(board.ProgramId, FuncCodes.Delete) || (mine && canCreate),
            CanManage: manager,
            CanComment: board.CommentYn == "Y" && canCreate,
            CanDeleteAny: granted.Can(board.ProgramId, FuncCodes.Delete)));
    }

    /// <summary>글쓰기·수정·답글 화면. <paramref name="id"/>가 있으면 수정, <paramref name="parent"/>가 있으면 답글.</summary>
    [HttpGet("Edit")]
    public async Task<IActionResult> Edit(string boardCd, long? id, long? parent)
    {
        if (await FindAsync(boardCd) is not { } board)
        {
            return NotFound();
        }

        var granted = await DemandAsync(board, FuncCodes.Create, FuncCodes.Update);
        var manager = granted.Can(board.ProgramId, FuncCodes.Update);
        var cancel = HttpContext.RequestAborted;
        try
        {
            PostDetail? existing = null;
            PostDto? parentPost = null;
            if (id is { } postId)
            {
                existing = await boards.GetPostAsync(postId, countView: false, manager, cancel);
                if (!string.Equals(existing.Post.BoardCd, board.BoardCd, StringComparison.OrdinalIgnoreCase)
                    || !(manager || (existing.Post.MineYn == "Y" && granted.Can(board.ProgramId, FuncCodes.Create))))
                {
                    return NotFound();
                }
            }
            else
            {
                await DemandAsync(board, FuncCodes.Create);
                if (parent is { } parentId)
                {
                    if (board.ReplyYn != "Y")
                    {
                        return NotFound();
                    }

                    parentPost = (await boards.GetPostAsync(parentId, countView: false, manager, cancel)).Post;
                    if (!string.Equals(parentPost.BoardCd, board.BoardCd, StringComparison.OrdinalIgnoreCase))
                    {
                        return NotFound();
                    }
                }
            }

            var companies = manager ? await boards.GetTargetCompaniesAsync(cancel) : [];
            return View(new BoardEditModel(board, existing?.Post, existing?.Targets ?? [], parentPost, manager, companies));
        }
        catch (BizException ex) when (ex.MessageKey == MessageKeys.NotFound)
        {
            return NotFound();
        }
    }

    /// <summary>글 저장. 새 글·답글은 CREATE, 수정은 CREATE(본인 글) 또는 UPDATE(남의 글). 글 ID를 돌려준다.</summary>
    [HttpPost("Save")]
    public async Task<ApiResult<long>> Save(string boardCd, [FromBody] PostSaveRequest request)
    {
        var board = await RequireAsync(boardCd);
        var granted = await DemandAsync(board, request.PostId is null ? [FuncCodes.Create] : [FuncCodes.Create, FuncCodes.Update]);
        return Ok(await boards.SaveAsync(board, request, granted.Can(board.ProgramId, FuncCodes.Update), HttpContext.RequestAborted));
    }

    /// <summary>글 삭제. 본인 글은 CREATE, 남의 글은 DELETE.</summary>
    [HttpPost("Delete")]
    public async Task<ApiResult> Delete(string boardCd, [FromBody] PostDeleteRequest request)
    {
        var board = await RequireAsync(boardCd);
        var granted = await DemandAsync(board, FuncCodes.Create, FuncCodes.Delete);
        await boards.DeleteAsync(board.BoardCd, request, granted.Can(board.ProgramId, FuncCodes.Delete), HttpContext.RequestAborted);
        return Ok();
    }

    /// <summary>조회자 목록 (조회자 목록을 켠 게시판, UPDATE).</summary>
    [HttpGet("Readers")]
    public async Task<ApiResult<IReadOnlyList<PostReaderDto>>> Readers(string boardCd, long id)
    {
        var board = await RequireAsync(boardCd);
        await DemandAsync(board, FuncCodes.Update);
        return Ok(board.ReaderYn == "Y"
            ? await boards.GetReadersAsync(board.BoardCd, id, HttpContext.RequestAborted)
            : throw new BizException(MessageKeys.NotFound));
    }

    /// <summary>댓글 목록.</summary>
    [HttpGet("Comments")]
    public async Task<ApiResult<IReadOnlyList<CommentDto>>> Comments(string boardCd, long id)
    {
        var board = await RequireAsync(boardCd);
        var granted = await DemandAsync(board, FuncCodes.Read);
        return Ok(await boards.GetCommentsAsync(board.BoardCd, id, granted.Can(board.ProgramId, FuncCodes.Update), HttpContext.RequestAborted));
    }

    /// <summary>댓글 저장 (CREATE). 본인 댓글만 고칠 수 있다.</summary>
    [HttpPost("CommentSave")]
    public async Task<ApiResult<long>> CommentSave(string boardCd, [FromBody] CommentSaveRequest request)
    {
        var board = await RequireAsync(boardCd);
        var granted = await DemandAsync(board, FuncCodes.Create);
        return Ok(await boards.SaveCommentAsync(board.BoardCd, request, granted.Can(board.ProgramId, FuncCodes.Update), HttpContext.RequestAborted));
    }

    /// <summary>댓글 삭제. 본인 댓글은 CREATE, 남의 댓글은 DELETE.</summary>
    [HttpPost("CommentDelete")]
    public async Task<ApiResult> CommentDelete(string boardCd, [FromBody] CommentDeleteRequest request)
    {
        var board = await RequireAsync(boardCd);
        var granted = await DemandAsync(board, FuncCodes.Create, FuncCodes.Delete);
        await boards.DeleteCommentAsync(board.BoardCd, request, granted.Can(board.ProgramId, FuncCodes.Delete), HttpContext.RequestAborted);
        return Ok();
    }

    private async Task<BoardDto?> FindAsync(string boardCd) =>
        BoardCodePattern().IsMatch(boardCd) ? await boards.GetBoardAsync(boardCd, HttpContext.RequestAborted) : null;

    private async Task<BoardDto> RequireAsync(string boardCd) =>
        await FindAsync(boardCd) ?? throw new BizException(MessageKeys.NotFound);

    /// <summary>게시판 프로그램의 기능 중 하나라도 있는지 확인하고(없으면 403), 권한을 돌려준다.</summary>
    private async Task<Portal.Security.Authorization.PermissionSet> DemandAsync(BoardDto board, params string[] funcs)
    {
        await DemandAnyFuncAsync(board.ProgramId, funcs);
        return await permissions.GetAsync(HttpContext.RequestAborted);
    }
}
