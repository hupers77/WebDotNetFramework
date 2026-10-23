using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Data;
using Portal.Infrastructure.Html;
using Portal.Web.Areas.Board.Models;

namespace Portal.Web.Areas.Board.Services;

/// <summary>
/// 게시판 (<c>BRD_*</c> 프로그램). 게시판 설정에 따라 글·답글·댓글·조회자 목록을 다룬다.
/// 읽기·쓰기 권한은 게시판의 프로그램에 대해 컨트롤러가 확인하고, 글 단위 규칙(게시기간, 공개 대상 회사, 비밀글, 본인 글)은 SP가 지킨다.
/// </summary>
/// <remarks><c>manager</c>는 게시판 프로그램의 수정(UPDATE) 권한이 있는 사람이다(남의 글을 고치고 상단 고정·공개 대상을 정한다). 삭제는 호출하는 쪽이 삭제(DELETE) 권한으로 정해 넘긴다.</remarks>
public interface IBoardService
{
    /// <summary>게시판 설정. 없거나 사용 안 하는 게시판이면 null.</summary>
    Task<BoardDto?> GetBoardAsync(string boardCd, CancellationToken cancellationToken = default);

    /// <summary>글 목록.</summary>
    Task<PageResult<PostRowDto>> GetListAsync(string boardCd, PostSearch search, bool manager, CancellationToken cancellationToken = default);

    /// <summary>글 한 건. 볼 수 없으면 NOT_FOUND 오류. <paramref name="countView"/>가 true면 조회 수·조회자 기록을 남긴다.</summary>
    Task<PostDetail> GetPostAsync(long postId, bool countView, bool manager, CancellationToken cancellationToken = default);

    /// <summary>글을 등록하거나 고치고 글 ID를 돌려준다. 내용은 HTML 화이트리스트 필터를 거쳐 저장한다.</summary>
    Task<long> SaveAsync(BoardDto board, PostSaveRequest request, bool manager, CancellationToken cancellationToken = default);

    /// <summary>글을 지운다(원글이면 답글도).</summary>
    Task DeleteAsync(string boardCd, PostDeleteRequest request, bool manager, CancellationToken cancellationToken = default);

    /// <summary>조회자 목록 (조회자 목록을 켠 게시판).</summary>
    Task<IReadOnlyList<PostReaderDto>> GetReadersAsync(string boardCd, long postId, CancellationToken cancellationToken = default);

    /// <summary>댓글 목록.</summary>
    Task<IReadOnlyList<CommentDto>> GetCommentsAsync(long postId, bool manager, CancellationToken cancellationToken = default);

    /// <summary>댓글을 등록하거나 고치고 댓글 ID를 돌려준다.</summary>
    Task<long> SaveCommentAsync(CommentSaveRequest request, bool manager, CancellationToken cancellationToken = default);

    /// <summary>댓글을 지운다.</summary>
    Task DeleteCommentAsync(CommentDeleteRequest request, bool manager, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class BoardService(ISpExecutor sp) : IBoardService
{
    internal const string BoardSp = "FW.USP_BOARD_GET";
    internal const string ListSp = "FW.USP_POST_GET_LIST";
    internal const string GetSp = "FW.USP_POST_GET";
    internal const string SaveSp = "FW.USP_POST_SAVE";
    internal const string DeleteSp = "FW.USP_POST_DEL";
    internal const string ReaderSp = "FW.USP_POST_READER_GET_LIST";
    internal const string CommentListSp = "FW.USP_COMMENT_GET_LIST";
    internal const string CommentSaveSp = "FW.USP_COMMENT_SAVE";
    internal const string CommentDeleteSp = "FW.USP_COMMENT_DEL";

    /// <summary>Y/N 표시.</summary>
    internal static string YesNo(bool value) => value ? "Y" : "N";

    /// <inheritdoc />
    public Task<BoardDto?> GetBoardAsync(string boardCd, CancellationToken cancellationToken = default) =>
        sp.QuerySingleAsync<BoardDto>(BoardSp, new { P_BOARD_CD = boardCd }, cancellationToken);

    /// <inheritdoc />
    public Task<PageResult<PostRowDto>> GetListAsync(string boardCd, PostSearch search, bool manager, CancellationToken cancellationToken = default) =>
        sp.QueryPageAsync<PostRowDto>(
            ListSp,
            new
            {
                P_BOARD_CD = boardCd,
                P_SEARCH_TYPE = search.SearchType,
                P_KEYWORD = string.IsNullOrWhiteSpace(search.Keyword) ? null : search.Keyword.Trim(),
                P_MANAGER_YN = YesNo(manager),
                P_PAGE_NO = search.PageNo,
                P_PAGE_SIZE = search.PageSize,
            },
            cancellationToken);

    /// <inheritdoc />
    public async Task<PostDetail> GetPostAsync(long postId, bool countView, bool manager, CancellationToken cancellationToken = default)
    {
        var (post, targets) = await sp.QueryMultipleAsync(
            GetSp,
            new { P_POST_ID = postId, P_COUNT_YN = YesNo(countView), P_MANAGER_YN = YesNo(manager) },
            async reader => (await reader.ReadSingleAsync<PostDto>(cancellationToken), await reader.ReadAsync<PostTargetRow>(cancellationToken)),
            cancellationToken);
        return post is null
            ? throw new BizException(MessageKeys.NotFound)
            : new PostDetail(post, [.. targets.Select(t => t.CompanyCd)]);
    }

    /// <inheritdoc />
    public async Task<long> SaveAsync(BoardDto board, PostSaveRequest request, bool manager, CancellationToken cancellationToken = default)
    {
        var title = request.Title.Trim();
        var content = HtmlSanitizer.Sanitize(request.Content);
        if (title.Length == 0 || HtmlSanitizer.ToPlainText(content).Length == 0)
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        if (request.PostId is not null && request.RowVer is null)
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        if (request.StartDt is { } start && request.EndDt is { } end && start > end)
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        var targets = request.Targets?.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (targets is not null && targets.Exists(t => t.Length > 10))
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        var result = await sp.ExecuteAsync(
            SaveSp,
            new
            {
                P_POST_ID = request.PostId,
                P_ROW_VER = request.RowVer,
                P_BOARD_CD = board.BoardCd,
                P_PARENT_POST_ID = request.ParentPostId,
                P_TITLE = title,
                P_CONTENT = content,
                P_SECRET_YN = request.SecretYn,
                P_PIN_YN = request.PinYn,
                P_START_DT = request.StartDt,
                P_END_DT = request.EndDt,
                P_FILE_GROUP_ID = request.FileGroupId,
                P_TARGETS = targets is null ? null : System.Text.Json.JsonSerializer.Serialize(targets),
                P_MANAGER_YN = YesNo(manager),
            },
            cancellationToken);
        return long.Parse(result.NewKey ?? request.PostId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string boardCd, PostDeleteRequest request, bool manager, CancellationToken cancellationToken = default) =>
        sp.ExecuteAsync(
            DeleteSp,
            new { P_POST_ID = request.PostId, P_ROW_VER = request.RowVer, P_BOARD_CD = boardCd, P_MANAGER_YN = YesNo(manager) },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<PostReaderDto>> GetReadersAsync(string boardCd, long postId, CancellationToken cancellationToken = default) =>
        sp.QueryAsync<PostReaderDto>(ReaderSp, new { P_POST_ID = postId, P_BOARD_CD = boardCd }, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<CommentDto>> GetCommentsAsync(long postId, bool manager, CancellationToken cancellationToken = default) =>
        sp.QueryAsync<CommentDto>(CommentListSp, new { P_POST_ID = postId, P_MANAGER_YN = YesNo(manager) }, cancellationToken);

    /// <inheritdoc />
    public async Task<long> SaveCommentAsync(CommentSaveRequest request, bool manager, CancellationToken cancellationToken = default)
    {
        var content = request.Content.Trim();
        if (content.Length == 0 || (request.CommentId is not null && request.RowVer is null))
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        var result = await sp.ExecuteAsync(
            CommentSaveSp,
            new { P_COMMENT_ID = request.CommentId, P_ROW_VER = request.RowVer, P_POST_ID = request.PostId, P_CONTENT = content, P_MANAGER_YN = YesNo(manager) },
            cancellationToken);
        return long.Parse(result.NewKey ?? "0", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public Task DeleteCommentAsync(CommentDeleteRequest request, bool manager, CancellationToken cancellationToken = default) =>
        sp.ExecuteAsync(
            CommentDeleteSp,
            new { P_COMMENT_ID = request.CommentId, P_ROW_VER = request.RowVer, P_POST_ID = request.PostId, P_MANAGER_YN = YesNo(manager) },
            cancellationToken);
}
