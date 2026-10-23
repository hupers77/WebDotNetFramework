using System.Text.Json;
using Portal.Core.Errors;
using Portal.Data;
using Portal.Testing;
using Portal.Web.Areas.Board.Models;
using Portal.Web.Areas.Board.Services;

namespace Portal.UnitTests.Board;

/// <summary>게시판 관리 Service의 입력 규칙: 수정 행만, 동시성 값 필수, Y/N만 허용하고 SP에는 JSON으로 넘긴다.</summary>
public sealed class BoardAdminServiceTests
{
    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 0x1A];
    private readonly FakeSpExecutor _sp = new();
    private readonly BoardAdminService _service;

    public BoardAdminServiceTests()
    {
        _sp.Returns(BoardAdminService.SaveSp, new SaveResult(1, null));
        _service = new BoardAdminService(_sp);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BoardAdminSaveRow Row(string code = "Notice", string rowStat = "U", byte[]? rowVer = null) => new()
    {
        RowStat = rowStat, BoardCd = code, NameKo = " 공지 ", NameEn = "Notices", NameZh = "公告", ReplyYn = "Y", SortNo = 10, RowVer = rowVer ?? RowVer,
    };

    private static BoardAdminSaveRequest Request(params BoardAdminSaveRow[] rows) => new() { Rows = [.. rows] };

    [Fact]
    public async Task SaveAsync_이름은_공백을_빼고_RowVer는_16진수_JSON으로_넘긴다()
    {
        var result = await _service.SaveAsync(Request(Row()), Ct);

        var json = JsonDocument.Parse((string)_sp.LastCall(BoardAdminService.SaveSp).Parameters["P_ROWS_JSON"]!).RootElement[0];
        Assert.Equal(1, result.AffectedCnt);
        Assert.Equal("공지", json.GetProperty("nameKo").GetString());
        Assert.Equal("Y", json.GetProperty("replyYn").GetString());
        Assert.Equal("000000000000001A", json.GetProperty("rowVer").GetString());
    }

    [Fact]
    public async Task SaveAsync_빈_요청은_변경없음()
    {
        var ex = await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(), Ct));

        Assert.Equal("MSG.NO_CHANGES", ex.MessageKey);
    }

    [Theory]
    [InlineData("I")]
    [InlineData("D")]
    public async Task SaveAsync_수정_행만_허용(string rowStat)
    {
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(Row(rowStat: rowStat)), Ct));

        Assert.Empty(_sp.Calls);
    }

    [Fact]
    public async Task SaveAsync_RowVer가_없거나_같은_게시판이_두번이면_거절()
    {
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(Row(rowVer: [1, 2])), Ct));
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(Row(), Row()), Ct));

        Assert.Empty(_sp.Calls);
    }

    [Fact]
    public async Task SaveAsync_YN이_아닌_값은_거절()
    {
        var row = Row();
        row.PinYn = "X";

        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(row), Ct));

        Assert.Empty(_sp.Calls);
    }
}
