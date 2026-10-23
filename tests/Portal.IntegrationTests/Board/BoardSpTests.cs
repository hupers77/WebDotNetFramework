using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.IntegrationTests.Infrastructure;
using Portal.Web.Areas.Board.Models;
using Portal.Web.Areas.Board.Services;

namespace Portal.IntegrationTests.Board;

/// <summary>
/// 게시판 SP: 게시판 설정·이름, 글 등록/수정/삭제(본인·관리자), 게시기간·공개 대상 회사·비밀글 가림, 답글(설정·단계), 조회 수·조회자,
/// 익명 게시판, 댓글, 첨부 묶음 확정, 감사 로그.
/// </summary>
/// <remarks>SP 오류는 테스트 트랜잭션도 롤백하므로 오류 확인은 각 테스트의 마지막 호출로 둔다.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class BoardSpTests(DbFixture fixture)
{
    private const string Code = "ItBoard";
    private const string Writer = "it.b.writer";
    private const string Other = "it.b.other";
    private const string Admin = "it.b.admin";
    private const string Partner1 = "IT_BP1";
    private const string Partner2 = "IT_BP2";
    private const string P1User = "it.b.p1";
    private const string P2User = "it.b.p2";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>본사 사용자 3명(작성자·다른 사람·관리자)과 협력사 2곳의 사용자, 설정을 모두 켠 시험 게시판을 만든다. 현재 사용자는 작성자.</summary>
    private static async Task<(TestDb Db, BoardService Service)> BeginAsync(DbFixture fixture, string flags = "'Y','Y','Y','N','Y','Y','Y','Y'")
    {
        var db = await fixture.BeginAsync();
        await db.SeedCompanyAsync(Partner1);
        await db.SeedCompanyAsync(Partner2);
        await db.SeedUserAsync(Writer, "HQ", "EMP");
        await db.SeedUserAsync(Other, "HQ", "EMP");
        await db.SeedUserAsync(Admin, "HQ", "EMP");
        await db.SeedUserAsync(P1User, Partner1, "PARTNER");
        await db.SeedUserAsync(P2User, Partner2, "PARTNER");
        await db.ExecAsync("INSERT INTO FW.TB_PROGRAM (PROGRAM_ID, MODULE_CD, PROGRAM_NM, URL) VALUES ('IT_BRD', 'BRD', N'시험 게시판', '/Board/ItBoard')");
        await db.ExecAsync(
            "INSERT INTO FW.TB_BOARD (BOARD_CD, PROGRAM_ID, BOARD_TYPE_CD, REPLY_YN, COMMENT_YN, ATTACH_YN, ANON_YN, SECRET_YN, PERIOD_YN, PIN_YN, READER_YN) " +
            $"VALUES ('{Code}', 'IT_BRD', 'NORMAL', {flags})");
        await db.ExecAsync("INSERT INTO FW.TB_BOARD_LANG (BOARD_CD, LANG_CD, BOARD_NM) VALUES (@C, 'ko', N'시험 게시판')", ("@C", Code));
        db.AsUser(Writer, "HQ", "EMP");
        return (db, new BoardService(db.Sp));
    }

    private static PostSaveRequest Post(string title = "제목", string content = "<p>본문</p>") => new() { Title = title, Content = content };

    private static async Task<long> WriteAsync(TestDb db, BoardService service, string user, string company, string type, PostSaveRequest request, bool manager = false)
    {
        db.AsUser(user, company, type);
        return await service.SaveAsync(new BoardDto { BoardCd = Code }, request, manager, Ct);
    }

    private static Task<long> WriteAsync(TestDb db, BoardService service, PostSaveRequest request, bool manager = false) =>
        WriteAsync(db, service, Writer, "HQ", "EMP", request, manager);

    private static async Task<IReadOnlyList<PostRowDto>> ListAsync(TestDb db, BoardService service, string user, string company, string type, bool manager = false, PostSearch? search = null)
    {
        db.AsUser(user, company, type);
        return (await service.GetListAsync(Code, search ?? new PostSearch(), manager, Ct)).Items;
    }

    private static async Task<PostDto> ReadAsync(TestDb db, BoardService service, long id, string user, string company, string type, bool manager = false, bool count = false)
    {
        db.AsUser(user, company, type);
        return (await service.GetPostAsync(id, count, manager, Ct)).Post;
    }

    [Fact]
    public async Task 시드게시판_네곳이_프로그램과_이름을_가진다()
    {
        await using var db = await fixture.BeginAsync();
        db.AsUser("sysadmin", "HQ", "EMP");
        var service = new BoardService(db.Sp);

        var notice = await service.GetBoardAsync("Notice", Ct);
        var qna = await service.GetBoardAsync("Qna", Ct);

        Assert.Equal("BRD_NOTICE", notice?.ProgramId);
        Assert.Equal("공지사항", notice?.BoardNm);
        Assert.Equal(("Y", "Y", "Y"), (notice?.PinYn, notice?.PeriodYn, notice?.ReaderYn));
        Assert.Equal(("Y", "Y", "Y"), (qna?.ReplyYn, qna?.CommentYn, qna?.SecretYn));
        Assert.Equal(4, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_BOARD WHERE BOARD_CD IN ('Notice','Archive','Qna','EmpFree')"));
    }

    [Fact]
    public async Task 사용안하는_게시판은_설정이_없다()
    {
        var (db, service) = await BeginAsync(fixture);
        await using var _ = db;
        await db.ExecAsync("UPDATE FW.TB_BOARD SET USE_YN = 'N' WHERE BOARD_CD = @C", ("@C", Code));

        Assert.Null(await service.GetBoardAsync(Code, Ct));
    }

    [Fact]
    public async Task 등록하면_목록과_본문에서_보이고_내용은_거른다()
    {
        var (db, service) = await BeginAsync(fixture);
        await using var _ = db;

        var id = await WriteAsync(db, service, Post("첫 글", "<p>안녕</p><script>alert(1)</script>"));
        var row = Assert.Single(await ListAsync(db, service, Other, "HQ", "EMP"));
        var post = await ReadAsync(db, service, id, Other, "HQ", "EMP");

        Assert.Equal(id, row.PostId);
        Assert.Equal(id, row.RootPostId);
        Assert.Equal("첫 글", row.Title);
        Assert.Equal(Writer, row.WriterNm);
        Assert.Equal("<p>안녕</p>", post.Content);
        Assert.Equal("HQ", post.CompanyCd);
    }

    [Fact]
    public async Task 조회수는_다른사람이_본문을_열때만_오르고_조회자가_남는다()
    {
        var (db, service) = await BeginAsync(fixture);
        await using var _ = db;
        var id = await WriteAsync(db, service, Post());

        await ReadAsync(db, service, id, Writer, "HQ", "EMP", count: true);
        await ReadAsync(db, service, id, Other, "HQ", "EMP", count: true);
        await ReadAsync(db, service, id, Other, "HQ", "EMP", count: true);
        await ReadAsync(db, service, id, P1User, Partner1, "PARTNER", count: false);

        Assert.Equal(2, await db.ScalarAsync<int>("SELECT VIEW_CNT FROM FW.TB_POST WHERE POST_ID = @I", ("@I", id)));
        Assert.Equal(2, await db.ScalarAsync<int>("SELECT READ_CNT FROM FW.TB_POST_READ WHERE POST_ID = @I AND USER_ID = @U", ("@I", id), ("@U", Other)));
        db.AsUser(Admin, "HQ", "EMP");
        var readers = await service.GetReadersAsync(Code, id, Ct);
        Assert.Equal([Other, Writer], readers.Select(r => r.UserId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task 상단고정은_관리자만_반영된다()
    {
        var (db, service) = await BeginAsync(fixture);
        await using var _ = db;
        var pinnedByUser = Post("작성자 고정 시도");
        pinnedByUser.PinYn = "Y";
        var pinnedByAdmin = Post("관리자 고정");
        pinnedByAdmin.PinYn = "Y";

        await WriteAsync(db, service, Post("먼저"));
        await WriteAsync(db, service, pinnedByUser);
        await WriteAsync(db, service, Admin, "HQ", "EMP", pinnedByAdmin, manager: true);
        await WriteAsync(db, service, Post("나중"));
        var titles = (await ListAsync(db, service, Other, "HQ", "EMP")).Select(r => r.Title).ToList();

        Assert.Equal(["관리자 고정", "나중", "작성자 고정 시도", "먼저"], titles);
    }

    [Fact]
    public async Task 게시기간_밖의_글은_작성자와_관리자만_본다()
    {
        var (db, service) = await BeginAsync(fixture);
        await using var _ = db;
        var past = Post("지난 글");
        past.StartDt = new DateOnly(2000, 1, 1);
        past.EndDt = new DateOnly(2000, 1, 2);
        var current = Post("게시 중");
        current.StartDt = new DateOnly(2000, 1, 1);
        current.EndDt = new DateOnly(2999, 1, 1);
        await WriteAsync(db, service, past);
        await WriteAsync(db, service, current);

        var other = (await ListAsync(db, service, Other, "HQ", "EMP")).Select(r => r.Title).ToList();
        var mine = (await ListAsync(db, service, Writer, "HQ", "EMP")).Select(r => r.Title).Order(StringComparer.Ordinal).ToList();
        var manager = (await ListAsync(db, service, Admin, "HQ", "EMP", manager: true)).Select(r => r.Title).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(["게시 중"], other);
        Assert.Equal(["게시 중", "지난 글"], mine);
        Assert.Equal(["게시 중", "지난 글"], manager);
    }

    [Fact]
    public async Task 공개대상_회사가_지정된_글은_본사와_지정된_회사만_본다()
    {
        var (db, service) = await BeginAsync(fixture);
        await using var _ = db;
        var restricted = Post("협력사 1 전용");
        restricted.Targets = [Partner1];
        await WriteAsync(db, service, Admin, "HQ", "EMP", restricted, manager: true);
        await WriteAsync(db, service, Admin, "HQ", "EMP", Post("모두에게"), manager: true);

        var p1 = (await ListAsync(db, service, P1User, Partner1, "PARTNER")).Select(r => r.Title).Order(StringComparer.Ordinal).ToList();
        var p2 = (await ListAsync(db, service, P2User, Partner2, "PARTNER")).Select(r => r.Title).ToList();
        var hq = (await ListAsync(db, service, Other, "HQ", "EMP")).Select(r => r.Title).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(["모두에게", "협력사 1 전용"], p1);
        Assert.Equal(["모두에게"], p2);
        Assert.Equal(["모두에게", "협력사 1 전용"], hq);
    }

    [Fact]
    public async Task 공개대상은_일반_작성자가_보내도_반영되지_않는다()
    {
        var (db, service) = await BeginAsync(fixture);
        await using var _ = db;
        var request = Post("제한 시도");
        request.Targets = [Partner1];

        var id = await WriteAsync(db, service, request);

        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_POST_TARGET WHERE POST_ID = @I", ("@I", id)));
    }

    [Fact]
    public async Task 비밀글은_목록에서_제목을_가리고_작성자와_관리자만_본문을_연다()
    {
        var (db, service) = await BeginAsync(fixture);
        await using var _ = db;
        var secret = Post("비밀 제목", "<p>비밀 내용</p>");
        secret.SecretYn = "Y";
        var id = await WriteAsync(db, service, secret);

        var asOther = Assert.Single(await ListAsync(db, service, Other, "HQ", "EMP"));
        var byWriter = await ReadAsync(db, service, id, Writer, "HQ", "EMP");
        var byManager = await ReadAsync(db, service, id, Admin, "HQ", "EMP", manager: true);
        var found = await ListAsync(db, service, Other, "HQ", "EMP", search: new PostSearch { SearchType = "TITLE", Keyword = "비밀" });

        Assert.Null(asOther.Title);
        Assert.Equal("Y", asOther.SecretYn);
        Assert.Equal("비밀 제목", byWriter.Title);
        Assert.Equal("비밀 제목", byManager.Title);
        Assert.Empty(found);

        db.AsUser(Other, "HQ", "EMP");
        var error = await Assert.ThrowsAsync<BizException>(() => service.GetPostAsync(id, true, false, Ct));
        Assert.Equal(MessageKeys.NotFound, error.MessageKey);
    }

    [Fact]
    public async Task 답글은_원글_아래에_들여써지고_원글_작성자는_비밀_답글도_본다()
    {
        var (db, service) = await BeginAsync(fixture);
        await using var _ = db;
        var root = await WriteAsync(db, service, Post("질문"));
        var reply = Post("답변", "<p>답</p>");
        reply.ParentPostId = root;
        reply.SecretYn = "Y";
        var replyId = await WriteAsync(db, service, Admin, "HQ", "EMP", reply, manager: true);
        var second = Post("다른 질문");
        await WriteAsync(db, service, Other, "HQ", "EMP", second);

        var rows = await ListAsync(db, service, Writer, "HQ", "EMP");
        var secretReply = await ReadAsync(db, service, replyId, Writer, "HQ", "EMP");

        Assert.Equal(["다른 질문", "질문", "답변"], rows.Select(r => r.Title));
        Assert.Equal([0, 0, 1], rows.Select(r => r.Depth));
        Assert.Equal(root, rows[2].RootPostId);
        Assert.Equal("답변", secretReply.Title);
        Assert.Equal(root, secretReply.ParentPostId);
    }

    [Fact]
    public async Task 답글을_막은_게시판에서는_답글을_쓸_수_없다()
    {
        var (db, service) = await BeginAsync(fixture, "'N','Y','Y','N','Y','Y','Y','Y'");
        await using var _ = db;
        var root = await WriteAsync(db, service, Post());
        var reply = Post("답글");
        reply.ParentPostId = root;

        var error = await Assert.ThrowsAsync<BizException>(() => WriteAsync(db, service, reply));

        Assert.Equal("MSG.BOARD_NO_REPLY", error.MessageKey);
    }

    [Fact]
    public async Task 답글은_세단계까지만_쓸_수_있다()
    {
        var (db, service) = await BeginAsync(fixture);
        await using var _ = db;
        var parent = await WriteAsync(db, service, Post("0"));
        for (var depth = 1; depth <= 3; depth++)
        {
            var reply = Post(depth.ToString(System.Globalization.CultureInfo.InvariantCulture));
            reply.ParentPostId = parent;
            parent = await WriteAsync(db, service, reply);
        }

        var tooDeep = Post("4");
        tooDeep.ParentPostId = parent;

        var error = await Assert.ThrowsAsync<BizException>(() => WriteAsync(db, service, tooDeep));
        Assert.Equal("MSG.BOARD_REPLY_DEPTH", error.MessageKey);
    }

    [Fact]
    public async Task 수정은_작성자나_관리자만_하고_동시수정은_막는다()
    {
        var (db, service) = await BeginAsync(fixture);
        await using var _ = db;
        var id = await WriteAsync(db, service, Post("원래"));
        var current = await ReadAsync(db, service, id, Writer, "HQ", "EMP");
        var edit = Post("고침");
        edit.PostId = id;
        edit.RowVer = current.RowVer;
        await WriteAsync(db, service, edit);
        var byManager = Post("관리자가 고침");
        byManager.PostId = id;
        byManager.RowVer = (await ReadAsync(db, service, id, Writer, "HQ", "EMP")).RowVer;
        await WriteAsync(db, service, Admin, "HQ", "EMP", byManager, manager: true);

        Assert.Equal("관리자가 고침", (await ReadAsync(db, service, id, Other, "HQ", "EMP")).Title);

        var stale = Post("낡은 버전");
        stale.PostId = id;
        stale.RowVer = current.RowVer;
        var error = await Assert.ThrowsAsync<BizException>(() => WriteAsync(db, service, stale));
        Assert.Equal(MessageKeys.Concurrency, error.MessageKey);
    }

    [Fact]
    public async Task 남의_글은_일반_사용자가_고칠_수_없다()
    {
        var (db, service) = await BeginAsync(fixture);
        await using var _ = db;
        var id = await WriteAsync(db, service, Post());
        var edit = Post("가로채기");
        edit.PostId = id;
        edit.RowVer = (await ReadAsync(db, service, id, Writer, "HQ", "EMP")).RowVer;

        var error = await Assert.ThrowsAsync<BizException>(() => WriteAsync(db, service, Other, "HQ", "EMP", edit));

        Assert.Equal(MessageKeys.NotFound, error.MessageKey);
    }

    [Fact]
    public async Task 원글을_지우면_답글도_사라지고_감사로그가_남는다()
    {
        var (db, service) = await BeginAsync(fixture);
        await using var _ = db;
        var root = await WriteAsync(db, service, Post("질문"));
        var reply = Post("답변");
        reply.ParentPostId = root;
        var replyId = await WriteAsync(db, service, Other, "HQ", "EMP", reply);
        var other = Post("남는 글");
        await WriteAsync(db, service, Other, "HQ", "EMP", other);

        db.AsUser(Writer, "HQ", "EMP");
        await service.DeleteAsync(Code, new PostDeleteRequest { PostId = root, RowVer = (await ReadAsync(db, service, root, Writer, "HQ", "EMP")).RowVer }, manager: false, Ct);

        var titles = (await ListAsync(db, service, Other, "HQ", "EMP")).Select(r => r.Title).ToList();
        Assert.Equal(["남는 글"], titles);
        Assert.Equal(2, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_POST WHERE ROOT_POST_ID = @I AND DEL_YN = 'Y'", ("@I", root)));
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_POST' AND ACTION_CD = 'D' AND ROW_KEY = @K", ("@K", root.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        db.AsUser(Other, "HQ", "EMP");
        var error = await Assert.ThrowsAsync<BizException>(() => service.GetPostAsync(replyId, false, false, Ct));
        Assert.Equal(MessageKeys.NotFound, error.MessageKey);
    }

    [Fact]
    public async Task 남의_글은_관리자가_아니면_지울_수_없다()
    {
        var (db, service) = await BeginAsync(fixture);
        await using var _ = db;
        var id = await WriteAsync(db, service, Post());
        var rowVer = (await ReadAsync(db, service, id, Writer, "HQ", "EMP")).RowVer;

        db.AsUser(Other, "HQ", "EMP");
        var error = await Assert.ThrowsAsync<BizException>(() => service.DeleteAsync(Code, new PostDeleteRequest { PostId = id, RowVer = rowVer }, manager: false, Ct));

        Assert.Equal(MessageKeys.NotFound, error.MessageKey);
    }

    [Fact]
    public async Task 익명게시판은_관리자와_본인_외에는_작성자를_숨긴다()
    {
        var (db, service) = await BeginAsync(fixture, "'N','Y','Y','Y','N','N','N','N'");
        await using var _ = db;
        var id = await WriteAsync(db, service, Post());
        db.AsUser(Other, "HQ", "EMP");
        await service.SaveCommentAsync(new CommentSaveRequest { PostId = id, Content = "댓글" }, manager: false, Ct);

        var asOther = Assert.Single(await ListAsync(db, service, Other, "HQ", "EMP"));
        var mine = Assert.Single(await ListAsync(db, service, Writer, "HQ", "EMP"));
        var asManager = Assert.Single(await ListAsync(db, service, Admin, "HQ", "EMP", manager: true));
        db.AsUser(Writer, "HQ", "EMP");
        var comment = Assert.Single(await service.GetCommentsAsync(id, manager: false, Ct));
        db.AsUser(Other, "HQ", "EMP");
        var ownComment = Assert.Single(await service.GetCommentsAsync(id, manager: false, Ct));

        Assert.Null(asOther.WriterNm);
        Assert.Equal(Writer, mine.WriterNm);
        Assert.Equal(Writer, asManager.WriterNm);
        Assert.Null(comment.WriterNm);
        Assert.Equal(Other, ownComment.WriterNm);
    }

    [Fact]
    public async Task 댓글은_등록_수정_삭제가_본인_것만_된다()
    {
        var (db, service) = await BeginAsync(fixture);
        await using var _ = db;
        var id = await WriteAsync(db, service, Post());
        db.AsUser(Other, "HQ", "EMP");
        var commentId = await service.SaveCommentAsync(new CommentSaveRequest { PostId = id, Content = "첫 댓글" }, manager: false, Ct);
        var created = Assert.Single(await service.GetCommentsAsync(id, manager: false, Ct));
        await service.SaveCommentAsync(new CommentSaveRequest { PostId = id, CommentId = commentId, RowVer = created.RowVer, Content = "고친 댓글" }, manager: false, Ct);

        db.AsUser(Writer, "HQ", "EMP");
        var seen = Assert.Single(await service.GetCommentsAsync(id, manager: false, Ct));
        Assert.Equal("고친 댓글", seen.Content);

        db.AsUser(Admin, "HQ", "EMP");
        await service.DeleteCommentAsync(new CommentDeleteRequest { PostId = id, CommentId = commentId, RowVer = seen.RowVer }, manager: true, Ct);
        Assert.Empty(await service.GetCommentsAsync(id, manager: true, Ct));
    }

    [Fact]
    public async Task 남의_댓글은_고칠_수_없다()
    {
        var (db, service) = await BeginAsync(fixture);
        await using var _ = db;
        var id = await WriteAsync(db, service, Post());
        db.AsUser(Other, "HQ", "EMP");
        var commentId = await service.SaveCommentAsync(new CommentSaveRequest { PostId = id, Content = "내 댓글" }, manager: false, Ct);
        var created = Assert.Single(await service.GetCommentsAsync(id, manager: false, Ct));

        db.AsUser(Admin, "HQ", "EMP");
        var error = await Assert.ThrowsAsync<BizException>(() =>
            service.SaveCommentAsync(new CommentSaveRequest { PostId = id, CommentId = commentId, RowVer = created.RowVer, Content = "관리자가 고침" }, manager: true, Ct));

        Assert.Equal(MessageKeys.NotFound, error.MessageKey);
    }

    [Fact]
    public async Task 댓글을_막은_게시판에서는_댓글이_등록되지_않는다()
    {
        var (db, service) = await BeginAsync(fixture, "'Y','N','Y','N','Y','Y','Y','Y'");
        await using var _ = db;
        var id = await WriteAsync(db, service, Post());

        var error = await Assert.ThrowsAsync<BizException>(() => service.SaveCommentAsync(new CommentSaveRequest { PostId = id, Content = "댓글" }, manager: false, Ct));

        Assert.Equal(MessageKeys.NotFound, error.MessageKey);
    }

    [Fact]
    public async Task 첨부묶음은_글을_저장할_때_확정하고_공유한다()
    {
        var (db, service) = await BeginAsync(fixture);
        await using var _ = db;
        var groupId = await db.ScalarAsync<long>(
            "INSERT INTO FW.TB_FILE_GROUP (PROGRAM_ID, COMPANY_CD) OUTPUT INSERTED.FILE_GROUP_ID VALUES ('IT_BRD', 'HQ')");
        var request = Post();
        request.FileGroupId = groupId;

        var id = await WriteAsync(db, service, request);
        var row = Assert.Single(await ListAsync(db, service, Other, "HQ", "EMP"));

        Assert.Equal("YY", await db.ScalarAsync<string>("SELECT CONFIRM_YN + SHARE_YN FROM FW.TB_FILE_GROUP WHERE FILE_GROUP_ID = @G", ("@G", groupId)));
        Assert.Equal("Y", row.HasFileYn);
        Assert.Equal(groupId, (await ReadAsync(db, service, id, Other, "HQ", "EMP")).FileGroupId);
    }

    [Fact]
    public async Task 첨부를_막은_게시판은_첨부묶음을_저장하지_않는다()
    {
        var (db, service) = await BeginAsync(fixture, "'Y','Y','N','N','Y','Y','Y','Y'");
        await using var _ = db;
        var groupId = await db.ScalarAsync<long>(
            "INSERT INTO FW.TB_FILE_GROUP (PROGRAM_ID, COMPANY_CD) OUTPUT INSERTED.FILE_GROUP_ID VALUES ('IT_BRD', 'HQ')");
        var request = Post();
        request.FileGroupId = groupId;

        var id = await WriteAsync(db, service, request);

        Assert.Null((await ReadAsync(db, service, id, Writer, "HQ", "EMP")).FileGroupId);
    }

    [Fact]
    public async Task 검색은_제목_내용_작성자로_찾는다()
    {
        var (db, service) = await BeginAsync(fixture);
        await using var _ = db;
        await WriteAsync(db, service, Post("서버 점검 안내", "<p>토요일 새벽</p>"));
        await WriteAsync(db, service, Post("휴가 안내", "<p>서버실 출입 제한</p>"));
        await WriteAsync(db, service, Other, "HQ", "EMP", Post("기타", "<p>없음</p>"));

        var title = await ListAsync(db, service, Admin, "HQ", "EMP", search: new PostSearch { SearchType = "TITLE", Keyword = "서버" });
        var both = await ListAsync(db, service, Admin, "HQ", "EMP", search: new PostSearch { SearchType = "TITLE_CONTENT", Keyword = "서버" });
        var writer = await ListAsync(db, service, Admin, "HQ", "EMP", search: new PostSearch { SearchType = "WRITER", Keyword = Other });

        Assert.Equal(["서버 점검 안내"], title.Select(r => r.Title));
        Assert.Equal(2, both.Count);
        Assert.Equal(["기타"], writer.Select(r => r.Title));
    }

    [Fact]
    public async Task 쪽나눔은_전체건수와_쪽크기를_지킨다()
    {
        var (db, service) = await BeginAsync(fixture);
        await using var _ = db;
        for (var i = 1; i <= 5; i++)
        {
            await WriteAsync(db, service, Post($"글 {i}"));
        }

        db.AsUser(Other, "HQ", "EMP");
        var page = await service.GetListAsync(Code, new PostSearch { PageNo = 2, PageSize = 2 }, false, Ct);

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(["글 3", "글 2"], page.Items.Select(r => r.Title));
    }
}
