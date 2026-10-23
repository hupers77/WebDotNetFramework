using System.Text.Json;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Data;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Board.Models;

namespace Portal.Web.Areas.Board.Services;

/// <summary>게시판 관리 (<c>BRD_BOARD_MGT</c>). 게시판별 기능 설정과 이름을 고친다. 게시판 추가·삭제는 없다.</summary>
public interface IBoardAdminService
{
    /// <summary>게시판 목록 (사용 안 함 포함).</summary>
    Task<PageResult<BoardAdminDto>> GetListAsync(CancellationToken cancellationToken = default);

    /// <summary>바뀐 게시판을 한 번에 저장한다.</summary>
    Task<BoardAdminSaveResult> SaveAsync(BoardAdminSaveRequest request, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class BoardAdminService(ISpExecutor sp) : IBoardAdminService
{
    internal const string ListSp = "FW.USP_BOARD_ADMIN_GET_LIST";
    internal const string SaveSp = "FW.USP_BOARD_SAVE";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public Task<PageResult<BoardAdminDto>> GetListAsync(CancellationToken cancellationToken = default) =>
        sp.QueryPageAsync<BoardAdminDto>(ListSp, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<BoardAdminSaveResult> SaveAsync(BoardAdminSaveRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Rows.Count == 0)
        {
            throw new BizException(MessageKeys.NoChanges);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<object>(request.Rows.Count);
        foreach (var row in request.Rows)
        {
            if (row.RowStat != RowStates.Updated || row.RowVer is not { Length: 8 } || !seen.Add(row.BoardCd)
                || new[] { row.ReplyYn, row.CommentYn, row.AttachYn, row.AnonYn, row.SecretYn, row.PeriodYn, row.PinYn, row.ReaderYn, row.UseYn }
                    .Any(v => v is not ("Y" or "N")))
            {
                throw new BizException(MessageKeys.InvalidInput);
            }

            rows.Add(new
            {
                row.BoardCd,
                NameKo = row.NameKo.Trim(),
                NameEn = row.NameEn.Trim(),
                NameZh = row.NameZh.Trim(),
                row.ReplyYn, row.CommentYn, row.AttachYn, row.AnonYn, row.SecretYn, row.PeriodYn, row.PinYn, row.ReaderYn,
                row.SortNo, row.UseYn,
                RowVer = Convert.ToHexString(row.RowVer),
            });
        }

        var saved = await sp.ExecuteAsync(SaveSp, new { P_ROWS_JSON = JsonSerializer.Serialize(rows, Json) }, cancellationToken);
        return new BoardAdminSaveResult(saved.AffectedCnt);
    }
}
