using System.Net;
using System.Text.Json;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Data;
using Portal.Web.Areas.Board.Models;
using Portal.Web.Areas.Board.Services;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>
/// 게시판 화면: 게시판 설정에 따라 보이는 버튼(글쓰기·답글·수정·삭제·조회자·댓글)이 권한별로 달라지고, 없는 게시판·볼 수 없는 글은 404,
/// 권한 없는 요청은 403, 글 내용은 서버가 한 번 더 걸러 그리며, 관리자 여부(수정 권한)가 SP로 넘어간다.
/// </summary>
public sealed class BoardScreenTests : IClassFixture<PortalWebFactory>
{
    private const string Program = "BRD_TEST";
    private const string Home = "DSH_HOME:READ";
    private static readonly string[] Reader = [Home, $"{Program}:READ"];
    private static readonly string[] Writer = [Home, $"{Program}:READ", $"{Program}:CREATE"];
    private static readonly string[] Manager = [Home, $"{Program}:READ", $"{Program}:CREATE", $"{Program}:UPDATE", $"{Program}:DELETE"];
    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 7];
    private readonly PortalWebFactory _web;

    public BoardScreenTests(PortalWebFactory web)
    {
        _web = web;
        web.Sp.Returns(BoardService.BoardSp, p => (string?)p["P_BOARD_CD"] switch
        {
            "Test" => Board(),
            "NoReply" => NoReplyBoard(),
            _ => null,
        });
        web.Sp.Returns(BoardService.GetSp, p => (long)p["P_POST_ID"]! switch
        {
            10 => PostResult(Post(10, mine: "N", content: "<p>본문</p><script>alert(1)</script><img src=x onerror=alert(2)>")),
            11 => PostResult(Post(11, mine: "Y")),
            12 => PostResult(Post(12, mine: "N", boardCd: "Other")),
            _ => throw new BizException(MessageKeys.NotFound),
        });
        web.Sp.Returns(BoardService.ListSp, new PageResult<PostRowDto> { Items = [], TotalCount = 0, PageNo = 1, PageSize = 20 });
        web.Sp.Returns(BoardService.SaveSp, new SaveResult(1, "55"));
        web.Sp.Returns(BoardService.DeleteSp, new SaveResult(1, null));
        web.Sp.Returns(BoardService.CommentListSp, new List<CommentDto>());
        web.Sp.Returns(BoardService.CommentSaveSp, new SaveResult(1, "9"));
        web.Sp.Returns(BoardService.CommentDeleteSp, new SaveResult(1, null));
        web.Sp.Returns(BoardService.ReaderSp, new List<PostReaderDto> { new() { UserId = "a", UserNm = "A", CompanyCd = "HQ", ReadCnt = 2 } });
        web.Sp.Returns(BoardService.TargetSp, new List<TargetCompanyDto> { new() { CompanyCd = "P1", CompanyNm = "협력사 1" } });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BoardDto Board() => new()
    {
        BoardCd = "Test", ProgramId = Program, BoardTypeCd = "NORMAL", BoardNm = "시험 게시판",
        ReplyYn = "Y", CommentYn = "Y", AttachYn = "N", SecretYn = "Y", PeriodYn = "Y", PinYn = "Y", ReaderYn = "Y",
    };

    private static BoardDto NoReplyBoard()
    {
        var board = Board();
        board.BoardCd = "NoReply";
        board.ReplyYn = "N";
        board.CommentYn = "N";
        board.ReaderYn = "N";
        return board;
    }

    private static PostDto Post(long id, string mine, string boardCd = "Test", string content = "<p>본문</p>") => new()
    {
        PostId = id, BoardCd = boardCd, RootPostId = id, Title = $"제목 {id}", Content = content, CompanyCd = "HQ", WriterNm = "작성자",
        MineYn = mine, RegDt = new DateTime(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc), UpdDt = new DateTime(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc), RowVer = RowVer,
    };

    private static object[] PostResult(PostDto post) => [new List<PostDto> { post }, new List<PostTargetRow>()];

    private async Task<HttpResponseMessage> GetAsync(string token, string url) => await _web.NewClient(token).GetAsync(url, Ct);

    private async Task<HttpResponseMessage> PostAsync(string token, string url, object body)
    {
        var (csrf, cookie) = await GetCsrfAsync(_web.NewClient(token), "/");
        var request = Ajax(HttpMethod.Post, url, body);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await _web.NewClient(token, cookie).SendAsync(request, Ct);
    }

    [Fact]
    public async Task 목록_글쓰기버튼은_CREATE권한이_있을때만_없는게시판은404_권한없으면403()
    {
        var writer = await (await GetAsync(_web.SignIn("bd.w", Writer), "/Board/Test?_layout=tab")).Content.ReadAsStringAsync(Ct);
        var reader = await (await GetAsync(_web.SignIn("bd.r", Reader), "/Board/Test?_layout=tab")).Content.ReadAsStringAsync(Ct);

        Assert.Contains("id=\"brd-write\"", writer, StringComparison.Ordinal);
        Assert.Contains("시험 게시판", writer, StringComparison.Ordinal);
        Assert.Contains("js/board/list.js", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"brd-write\"", reader, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(_web.SignIn("bd.n", Reader), "/Board/Nothing")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(_web.SignIn("bd.n2", Reader), "/Board/bad-code!")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(_web.SignIn("bd.x", [Home]), "/Board/Test")).StatusCode);
    }

    [Fact]
    public async Task 목록API_관리자여부는_UPDATE권한으로_정하고_접근기록에_게시판프로그램이_남는다()
    {
        var all = _web.SignIn("bd.l.m", Manager);
        var plain = _web.SignIn("bd.l.r", Reader);
        _web.DrainAccessLog();

        var asManager = await JsonAsync(await _web.NewClient(all).SendAsync(Ajax(HttpMethod.Get, "/Board/Test/List?searchType=TITLE&keyword=%20a%20&pageNo=2"), Ct));
        var managerCall = _web.Sp.LastCall(BoardService.ListSp).Parameters;
        await JsonAsync(await _web.NewClient(plain).SendAsync(Ajax(HttpMethod.Get, "/Board/Test/List"), Ct));

        Assert.True(asManager.GetProperty("success").GetBoolean(), asManager.ToString());
        Assert.Equal(("TITLE", "a", "Y", 2), ((string?)managerCall["P_SEARCH_TYPE"], (string?)managerCall["P_KEYWORD"], (string?)managerCall["P_MANAGER_YN"], (int?)managerCall["P_PAGE_NO"]));
        Assert.Equal("N", (string?)_web.Sp.LastCall(BoardService.ListSp).Parameters["P_MANAGER_YN"]);
        var log = _web.DrainAccessLog().First(e => e.RequestPath == "/Board/Test/List");
        Assert.Equal((Program, "READ"), (log.ProgramId, log.FuncCd));
    }

    [Fact]
    public async Task 목록API_잘못된_검색대상은_거절()
    {
        var json = await JsonAsync(await _web.NewClient(_web.SignIn("bd.l.bad", Reader)).SendAsync(Ajax(HttpMethod.Get, "/Board/Test/List?searchType=DROP"), Ct));

        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal(MessageKeys.InvalidInput, json.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task 글보기_내용은_다시_걸러서_그리고_조회수를_센다()
    {
        var html = await (await GetAsync(_web.SignIn("bd.v", Reader), "/Board/Test/View/10?_layout=tab")).Content.ReadAsStringAsync(Ct);

        Assert.Contains("<p>본문</p>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("alert(1)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("onerror", html, StringComparison.Ordinal);
        Assert.Equal("Y", (string?)_web.Sp.LastCall(BoardService.GetSp).Parameters["P_COUNT_YN"]);
    }

    [Fact]
    public async Task 글보기_버튼은_권한과_본인글여부에_따라_다르다()
    {
        var reader = await (await GetAsync(_web.SignIn("bd.v.r", Reader), "/Board/Test/View/10?_layout=tab")).Content.ReadAsStringAsync(Ct);
        var writerOwn = await (await GetAsync(_web.SignIn("bd.v.w1", Writer), "/Board/Test/View/11?_layout=tab")).Content.ReadAsStringAsync(Ct);
        var writerOther = await (await GetAsync(_web.SignIn("bd.v.w2", Writer), "/Board/Test/View/10?_layout=tab")).Content.ReadAsStringAsync(Ct);
        var manager = await (await GetAsync(_web.SignIn("bd.v.m", Manager), "/Board/Test/View/10?_layout=tab")).Content.ReadAsStringAsync(Ct);

        foreach (var id in new[] { "brd-reply", "brd-edit", "brd-delete", "brd-readers", "brd-comment-form" })
        {
            Assert.DoesNotContain($"id=\"{id}\"", reader, StringComparison.Ordinal);
        }

        Assert.Contains("id=\"brd-edit\"", writerOwn, StringComparison.Ordinal);
        Assert.Contains("id=\"brd-delete\"", writerOwn, StringComparison.Ordinal);
        Assert.Contains("id=\"brd-reply\"", writerOwn, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"brd-readers\"", writerOwn, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"brd-edit\"", writerOther, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"brd-delete\"", writerOther, StringComparison.Ordinal);
        Assert.Contains("id=\"brd-comment-form\"", writerOther, StringComparison.Ordinal);
        foreach (var id in new[] { "brd-reply", "brd-edit", "brd-delete", "brd-readers", "brd-comment-form" })
        {
            Assert.Contains($"id=\"{id}\"", manager, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task 글보기_답글과_댓글을_끈_게시판에는_해당_버튼이_없다()
    {
        _web.Sp.Returns(BoardService.GetSp, p => PostResult(Post((long)p["P_POST_ID"]!, mine: "Y", boardCd: "NoReply")));

        var html = await (await GetAsync(_web.SignIn("bd.v.nr", Manager), "/Board/NoReply/View/20?_layout=tab")).Content.ReadAsStringAsync(Ct);

        Assert.DoesNotContain("id=\"brd-reply\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"brd-readers\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("brd-comments", html, StringComparison.Ordinal);
        Assert.Contains("id=\"brd-edit\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 글보기_볼수없는글_다른게시판글은_404()
    {
        var token = _web.SignIn("bd.v.404", Reader);

        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(token, "/Board/Test/View/99")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(token, "/Board/Test/View/12")).StatusCode);
    }

    [Fact]
    public async Task 쓰기화면_새글은_CREATE가_필요하고_공개대상_상단고정은_관리자만()
    {
        var writer = await (await GetAsync(_web.SignIn("bd.e.w", Writer), "/Board/Test/Edit?_layout=tab")).Content.ReadAsStringAsync(Ct);
        var manager = await (await GetAsync(_web.SignIn("bd.e.m", Manager), "/Board/Test/Edit?_layout=tab")).Content.ReadAsStringAsync(Ct);

        Assert.Contains("id=\"brd-secret\"", writer, StringComparison.Ordinal);
        Assert.Contains("id=\"brd-start\"", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"brd-pin\"", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"brd-targets\"", writer, StringComparison.Ordinal);
        Assert.Contains("id=\"brd-pin\"", manager, StringComparison.Ordinal);
        Assert.Contains("value=\"P1\"", manager, StringComparison.Ordinal);
        Assert.Contains("contenteditable=\"true\"", writer, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(_web.SignIn("bd.e.r", Reader), "/Board/Test/Edit")).StatusCode);
    }

    [Fact]
    public async Task 쓰기화면_수정은_본인글이나_관리자만_답글은_설정과_원글이_맞아야한다()
    {
        var own = await GetAsync(_web.SignIn("bd.e.own", Writer), "/Board/Test/Edit?id=11&_layout=tab");
        var other = await GetAsync(_web.SignIn("bd.e.oth", Writer), "/Board/Test/Edit?id=10");
        var byManager = await GetAsync(_web.SignIn("bd.e.mgr", Manager), "/Board/Test/Edit?id=10&_layout=tab");
        var reply = await (await GetAsync(_web.SignIn("bd.e.rp", Writer), "/Board/Test/Edit?parent=10&_layout=tab")).Content.ReadAsStringAsync(Ct);
        var noReply = await GetAsync(_web.SignIn("bd.e.nr", Writer), "/Board/NoReply/Edit?parent=10");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byManager.StatusCode);
        Assert.Contains("RE: 제목 10", reply, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, noReply.StatusCode);
    }

    [Fact]
    public async Task 저장_새글은_CREATE_관리자여부는_UPDATE로_정한다()
    {
        var body = new { title = "제목", content = "<p>내용</p><script>x</script>", secretYn = "N", pinYn = "N" };

        var asWriter = await JsonAsync(await PostAsync(_web.SignIn("bd.s.w", Writer), "/Board/Test/Save", body));
        var plainCall = _web.Sp.LastCall(BoardService.SaveSp).Parameters;
        await PostAsync(_web.SignIn("bd.s.m", Manager), "/Board/Test/Save", body);

        Assert.Equal(55, asWriter.GetProperty("data").GetInt64());
        Assert.Equal(("N", "<p>내용</p>"), ((string?)plainCall["P_MANAGER_YN"], (string?)plainCall["P_CONTENT"]));
        Assert.Equal("Y", (string?)_web.Sp.LastCall(BoardService.SaveSp).Parameters["P_MANAGER_YN"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(_web.SignIn("bd.s.r", Reader), "/Board/Test/Save", body)).StatusCode);
    }

    [Fact]
    public async Task 저장_입력검사_빈제목_빈내용은_업무오류()
    {
        var token = _web.SignIn("bd.s.bad", Writer);

        var noTitle = await JsonAsync(await PostAsync(token, "/Board/Test/Save", new { title = "", content = "<p>a</p>" }));
        var noContent = await JsonAsync(await PostAsync(token, "/Board/Test/Save", new { title = "t", content = "<p> </p>" }));

        Assert.Equal(MessageKeys.InvalidInput, noTitle.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(MessageKeys.InvalidInput, noContent.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task 삭제_본인글은_CREATE_남의글은_DELETE_둘다없으면403()
    {
        var body = new { postId = 10, rowVer = Convert.ToBase64String(RowVer) };

        await PostAsync(_web.SignIn("bd.d.w", Writer), "/Board/Test/Delete", body);
        var plain = (string?)_web.Sp.LastCall(BoardService.DeleteSp).Parameters["P_MANAGER_YN"];
        await PostAsync(_web.SignIn("bd.d.m", Manager), "/Board/Test/Delete", body);

        Assert.Equal("N", plain);
        Assert.Equal("Y", (string?)_web.Sp.LastCall(BoardService.DeleteSp).Parameters["P_MANAGER_YN"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(_web.SignIn("bd.d.r", Reader), "/Board/Test/Delete", body)).StatusCode);
    }

    [Fact]
    public async Task 조회자목록은_UPDATE권한과_조회자목록설정이_필요하다()
    {
        var ok = await JsonAsync(await _web.NewClient(_web.SignIn("bd.rd.m", Manager)).SendAsync(Ajax(HttpMethod.Get, "/Board/Test/Readers?id=10"), Ct));
        var disabled = await JsonAsync(await _web.NewClient(_web.SignIn("bd.rd.m2", Manager)).SendAsync(Ajax(HttpMethod.Get, "/Board/NoReply/Readers?id=10"), Ct));

        Assert.Equal("a", ok.GetProperty("data")[0].GetProperty("userId").GetString());
        Assert.False(disabled.GetProperty("success").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await _web.NewClient(_web.SignIn("bd.rd.w", Writer)).SendAsync(Ajax(HttpMethod.Get, "/Board/Test/Readers?id=10"), Ct)).StatusCode);
    }

    [Fact]
    public async Task 댓글_등록은_CREATE_삭제는_본인CREATE_남의것DELETE_조회는_READ()
    {
        var comment = new { postId = 10, content = "댓글" };
        var list = await JsonAsync(await _web.NewClient(_web.SignIn("bd.c.r", Reader)).SendAsync(Ajax(HttpMethod.Get, "/Board/Test/Comments?id=10"), Ct));

        var saved = await JsonAsync(await PostAsync(_web.SignIn("bd.c.w", Writer), "/Board/Test/CommentSave", comment));
        await PostAsync(_web.SignIn("bd.c.d1", Writer), "/Board/Test/CommentDelete", new { commentId = 9, postId = 10, rowVer = Convert.ToBase64String(RowVer) });
        var plain = (string?)_web.Sp.LastCall(BoardService.CommentDeleteSp).Parameters["P_MANAGER_YN"];
        await PostAsync(_web.SignIn("bd.c.d2", Manager), "/Board/Test/CommentDelete", new { commentId = 9, postId = 10, rowVer = Convert.ToBase64String(RowVer) });

        Assert.True(list.GetProperty("success").GetBoolean());
        Assert.Equal(9, saved.GetProperty("data").GetInt64());
        Assert.Equal("N", plain);
        Assert.Equal("Y", (string?)_web.Sp.LastCall(BoardService.CommentDeleteSp).Parameters["P_MANAGER_YN"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(_web.SignIn("bd.c.x", Reader), "/Board/Test/CommentSave", comment)).StatusCode);
    }

    [Fact]
    public async Task 게시판관리_화면은_BOARD_MGT권한으로_열리고_저장버튼은_UPDATE권한이_있을때만()
    {
        _web.Sp.Returns(BoardAdminService.ListSp, new PageResult<BoardAdminDto> { Items = [], TotalCount = 0, PageNo = 1, PageSize = 500 });
        _web.Sp.Returns(BoardAdminService.SaveSp, new SaveResult(1, null));
        var admin = await (await GetAsync(_web.SignIn("bd.a.m", [Home, "BRD_BOARD_MGT:READ", "BRD_BOARD_MGT:UPDATE"]), "/Board/Admin?_layout=tab")).Content.ReadAsStringAsync(Ct);
        var reader = await (await GetAsync(_web.SignIn("bd.a.r", [Home, "BRD_BOARD_MGT:READ"]), "/Board/Admin?_layout=tab")).Content.ReadAsStringAsync(Ct);

        Assert.Contains("js/board/admin.js", admin, StringComparison.Ordinal);
        Assert.Contains("id=\"board-admin-save\"", admin, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"board-admin-save\"", reader, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(_web.SignIn("bd.a.x", [Home, Reader[1]]), "/Board/Admin")).StatusCode);
        var save = await PostAsync(_web.SignIn("bd.a.s", [Home, "BRD_BOARD_MGT:READ"]), "/Board/Admin/Save", new { rows = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Forbidden, save.StatusCode);
    }
}
