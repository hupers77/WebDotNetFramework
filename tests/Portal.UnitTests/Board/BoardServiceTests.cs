using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Data;
using Portal.Testing;
using Portal.Web.Areas.Board.Models;
using Portal.Web.Areas.Board.Services;

namespace Portal.UnitTests.Board;

/// <summary>게시판 Service의 입력 규칙: 내용은 HTML 필터를 거쳐 저장하고, 비어 있는 글·잘못된 기간은 SP를 부르기 전에 거절한다.</summary>
public sealed class BoardServiceTests
{
    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 1];
    private readonly FakeSpExecutor _sp = new();
    private readonly BoardService _service;
    private readonly BoardDto _board = new() { BoardCd = "Notice", ProgramId = "BRD_NOTICE", AttachYn = "Y" };

    public BoardServiceTests()
    {
        _sp.Returns(BoardService.SaveSp, new SaveResult(1, "42"));
        _service = new BoardService(_sp);
    }

    private static PostSaveRequest Request(string title = "제목", string content = "<p>본문</p>") => new() { Title = title, Content = content };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SaveAsync_내용은_필터를_거쳐_저장하고_새글ID를_돌려준다()
    {
        var id = await _service.SaveAsync(_board, Request(content: "<p onclick=\"x()\">본문</p><script>alert(1)</script>"), manager: false, Ct);

        var call = _sp.LastCall(BoardService.SaveSp);
        Assert.Equal(42, id);
        Assert.Equal("<p>본문</p>", call.Parameters["P_CONTENT"]);
        Assert.Equal("Notice", call.Parameters["P_BOARD_CD"]);
        Assert.Equal("N", call.Parameters["P_MANAGER_YN"]);
    }

    [Fact]
    public async Task SaveAsync_제목은_앞뒤공백을_뺀다()
    {
        await _service.SaveAsync(_board, Request(title: "  제목  "), manager: false, Ct);

        Assert.Equal("제목", _sp.LastCall(BoardService.SaveSp).Parameters["P_TITLE"]);
    }

    [Fact]
    public async Task SaveAsync_수정은_결과키가없어도_글ID를_유지한다()
    {
        _sp.Returns(BoardService.SaveSp, new SaveResult(1, null));
        var request = Request();
        request.PostId = 7;
        request.RowVer = RowVer;

        Assert.Equal(7, await _service.SaveAsync(_board, request, manager: true, Ct));
        Assert.Equal("Y", _sp.LastCall(BoardService.SaveSp).Parameters["P_MANAGER_YN"]);
    }

    [Theory]
    [InlineData("   ", "<p>본문</p>")]
    [InlineData("제목", "<p> </p>")]
    [InlineData("제목", "<script>alert(1)</script>")]
    [InlineData("제목", "<br><br>")]
    public async Task SaveAsync_제목이나_내용이_비면_SP를_부르지않고_거절(string title, string content)
    {
        var error = await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(_board, Request(title, content), manager: false, Ct));

        Assert.Equal(MessageKeys.InvalidInput, error.MessageKey);
        Assert.Empty(_sp.Calls);
    }

    [Fact]
    public async Task SaveAsync_수정은_RowVer가_필요하다()
    {
        var request = Request();
        request.PostId = 7;

        var error = await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(_board, request, manager: false, Ct));

        Assert.Equal(MessageKeys.InvalidInput, error.MessageKey);
    }

    [Fact]
    public async Task SaveAsync_게시기간이_거꾸로면_거절()
    {
        var request = Request();
        request.StartDt = new DateOnly(2026, 11, 2);
        request.EndDt = new DateOnly(2026, 11, 1);

        var error = await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(_board, request, manager: true, Ct));

        Assert.Equal(MessageKeys.InvalidInput, error.MessageKey);
    }

    [Fact]
    public async Task SaveAsync_공개대상은_공백과_중복을_정리해_JSON으로_보낸다()
    {
        var request = Request();
        request.Targets = [" P1 ", "P2", "P1", ""];

        await _service.SaveAsync(_board, request, manager: true, Ct);

        Assert.Equal("[\"P1\",\"P2\"]", _sp.LastCall(BoardService.SaveSp).Parameters["P_TARGETS"]);
    }

    [Fact]
    public async Task SaveAsync_공개대상을_안보내면_null이라_기존_대상을_유지한다()
    {
        await _service.SaveAsync(_board, Request(), manager: true, Ct);

        Assert.Null(_sp.LastCall(BoardService.SaveSp).Parameters["P_TARGETS"]);
    }

    [Fact]
    public async Task SaveAsync_회사코드가_너무길면_거절()
    {
        var request = Request();
        request.Targets = ["12345678901"];

        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(_board, request, manager: true, Ct));
    }

    [Fact]
    public async Task GetPostAsync_글이없으면_NOT_FOUND()
    {
        _sp.Returns(BoardService.GetSp, (IReadOnlyDictionary<string, object?> _) => new object[] { new List<PostDto>(), new List<PostTargetRow>() });

        var error = await Assert.ThrowsAsync<BizException>(() => _service.GetPostAsync(1, countView: true, manager: false, Ct));

        Assert.Equal(MessageKeys.NotFound, error.MessageKey);
    }

    [Fact]
    public async Task GetPostAsync_공개대상_회사코드를_함께_준다()
    {
        _sp.Returns(BoardService.GetSp, (IReadOnlyDictionary<string, object?> _) =>
            new object[] { new List<PostDto> { new() { PostId = 1, Title = "t" } }, new List<PostTargetRow> { new() { CompanyCd = "P1" } } });

        var detail = await _service.GetPostAsync(1, countView: false, manager: false, Ct);

        Assert.Equal(["P1"], detail.Targets);
        Assert.Equal("N", _sp.LastCall(BoardService.GetSp).Parameters["P_COUNT_YN"]);
    }

    [Fact]
    public async Task SaveCommentAsync_내용_공백은_거절하고_앞뒤공백은_뺀다()
    {
        _sp.Returns(BoardService.CommentSaveSp, new SaveResult(1, "9"));

        var id = await _service.SaveCommentAsync(new CommentSaveRequest { PostId = 1, Content = "  좋아요  " }, manager: false, Ct);
        var error = await Assert.ThrowsAsync<BizException>(() => _service.SaveCommentAsync(new CommentSaveRequest { PostId = 1, Content = "   " }, manager: false, Ct));

        Assert.Equal(9, id);
        Assert.Equal("좋아요", _sp.LastCall(BoardService.CommentSaveSp).Parameters["P_CONTENT"]);
        Assert.Equal(MessageKeys.InvalidInput, error.MessageKey);
    }

    [Fact]
    public async Task GetListAsync_검색어는_다듬고_빈검색어는_null()
    {
        _sp.Returns(BoardService.ListSp, new Portal.Core.Results.PageResult<PostRowDto> { Items = [], TotalCount = 0, PageNo = 1, PageSize = 20 });

        await _service.GetListAsync("Notice", new PostSearch { Keyword = "  결과  " }, manager: false, Ct);
        Assert.Equal("결과", _sp.LastCall(BoardService.ListSp).Parameters["P_KEYWORD"]);

        await _service.GetListAsync("Notice", new PostSearch { Keyword = "  " }, manager: true, Ct);
        Assert.Null(_sp.LastCall(BoardService.ListSp).Parameters["P_KEYWORD"]);
        Assert.Equal("Y", _sp.LastCall(BoardService.ListSp).Parameters["P_MANAGER_YN"]);
    }
}
