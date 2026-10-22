using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Data;
using Portal.IntegrationTests.Infrastructure;

namespace Portal.IntegrationTests.Fw;

/// <summary>공통코드 SP(목록·저장 표준 템플릿)와 SpExecutor의 실제 SQL Server 동작.</summary>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class CodeSpTests(DbFixture fixture)
{
    public sealed class CodeDto
    {
        public string Code { get; set; } = "";

        public string CodeNm { get; set; } = "";

        public int SortNo { get; set; }

        public string UseYn { get; set; } = "";

        public byte[] RowVer { get; set; } = [];
    }

    /// <summary>FW.TVP_CODE 컬럼 순서와 같은 순서로 선언한다.</summary>
    public sealed class CodeRow
    {
        public string RowStat { get; set; } = "I";

        public string Code { get; set; } = "";

        public string? CodeNm { get; set; }

        public string? ParentCode { get; set; }

        public int? SortNo { get; set; }

        public string? Attr1 { get; set; }

        public string? Attr2 { get; set; }

        public string? Attr3 { get; set; }

        public string? Attr4 { get; set; }

        public string? Attr5 { get; set; }

        public DateOnly? StartDt { get; set; }

        public DateOnly? EndDt { get; set; }

        public string? UseYn { get; set; }

        public byte[]? RowVer { get; set; }
    }

    [Fact]
    public async Task GetList_페이징_결과셋2개_전체건수()
    {
        await using var db = await fixture.BeginAsync();

        var page = await db.Sp.QueryPageAsync<CodeDto>("FW.USP_CODE_GET_LIST",
            new { P_GROUP_CD = "USER_STAT", P_PAGE_NO = 2, P_PAGE_SIZE = 4 }, TestContext.Current.CancellationToken);

        Assert.Equal(6, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(["EXPIRED", "RETIRED"], page.Items.Select(i => i.Code)); // 정렬 순서 PENDING, ACTIVE, LOCKED, DORMANT | EXPIRED, RETIRED
        Assert.All(page.Items, i => Assert.Equal(8, i.RowVer.Length));
    }

    [Theory]
    [InlineData("ko", "잠김")]
    [InlineData("en", "Locked")]
    [InlineData("zh-CN", "已锁定")]
    [InlineData("ja", "잠김")] // 번역이 없는 언어는 기본 언어로 대체
    public async Task GetList_사용자언어로명칭(string langCd, string expected)
    {
        await using var db = await fixture.BeginAsync();
        db.User.LangCd = langCd;

        var page = await db.Sp.QueryPageAsync<CodeDto>("FW.USP_CODE_GET_LIST",
            new { P_GROUP_CD = "USER_STAT", P_KEYWORD = "LOCK", P_PAGE_NO = 1, P_PAGE_SIZE = 50 }, TestContext.Current.CancellationToken);

        Assert.Equal(expected, Assert.Single(page.Items).CodeNm);
    }

    [Fact]
    public async Task Save_TVP등록수정삭제_건수반환_감사로그_캐시버전증가()
    {
        await using var db = await fixture.BeginAsync();
        await NewGroupAsync(db, "IT_GROUP");
        var cacheBefore = await db.ScalarAsync<long>("SELECT VERSION_NO FROM FW.TB_CACHE_VERSION WHERE CACHE_KEY = 'CODE'");

        var saved = await db.Sp.ExecuteAsync("FW.USP_CODE_SAVE", new
        {
            P_GROUP_CD = "IT_GROUP",
            P_ROWS = Tvp.From("FW.TVP_CODE", [new CodeRow { Code = "A", CodeNm = "에이", SortNo = 1 }, new CodeRow { Code = "B", CodeNm = "비", SortNo = 2 }]),
        }, TestContext.Current.CancellationToken);

        Assert.Equal(2, saved.AffectedCnt);
        Assert.Equal("it.admin", await db.ScalarAsync<string>("SELECT REG_ID FROM FW.TB_CODE WHERE GROUP_CD = 'IT_GROUP' AND CODE = 'A'"));

        var rows = await List(db, "IT_GROUP");
        var a = rows.Single(r => r.Code == "A");
        var b = rows.Single(r => r.Code == "B");
        saved = await db.Sp.ExecuteAsync("FW.USP_CODE_SAVE", new
        {
            P_GROUP_CD = "IT_GROUP",
            P_ROWS = Tvp.From("FW.TVP_CODE", [
                new CodeRow { RowStat = "U", Code = "A", CodeNm = "에이2", SortNo = 9, RowVer = a.RowVer },
                new CodeRow { RowStat = "D", Code = "B", RowVer = b.RowVer }]),
        }, TestContext.Current.CancellationToken);

        Assert.Equal(2, saved.AffectedCnt);
        rows = await List(db, "IT_GROUP");
        Assert.Equal(("에이2", 9), (rows.Single(r => r.Code == "A").CodeNm, rows.Single(r => r.Code == "A").SortNo));
        Assert.Equal("N", rows.Single(r => r.Code == "B").UseYn);
        Assert.Equal(cacheBefore + 2, await db.ScalarAsync<long>("SELECT VERSION_NO FROM FW.TB_CACHE_VERSION WHERE CACHE_KEY = 'CODE'"));
        Assert.Equal(2, await db.ScalarAsync<int>("SELECT COUNT(*) FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_CODE' AND ROW_KEY = 'IT_GROUP'"));
    }

    [Fact]
    public async Task Save_다른사용자가먼저수정_CONCURRENCY오류()
    {
        await using var db = await fixture.BeginAsync();
        await NewGroupAsync(db, "IT_GROUP");
        await db.Sp.ExecuteAsync("FW.USP_CODE_SAVE", new { P_GROUP_CD = "IT_GROUP", P_ROWS = Tvp.From("FW.TVP_CODE", [new CodeRow { Code = "A", CodeNm = "에이" }]) }, TestContext.Current.CancellationToken);
        var stale = (await List(db, "IT_GROUP")).Single().RowVer;
        await db.ExecAsync("UPDATE FW.TB_CODE SET SORT_NO = 5 WHERE GROUP_CD = 'IT_GROUP' AND CODE = 'A'");

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync("FW.USP_CODE_SAVE", new
        {
            P_GROUP_CD = "IT_GROUP",
            P_ROWS = Tvp.From("FW.TVP_CODE", [new CodeRow { RowStat = "U", Code = "A", CodeNm = "x", RowVer = stale }]),
        }, TestContext.Current.CancellationToken));

        Assert.Equal(MessageKeys.Concurrency, ex.MessageKey);
        Assert.Equal(50002, ex.SqlErrorNumber);
    }

    [Fact]
    public async Task Save_중복코드_DUP_CODE오류()
    {
        await using var db = await fixture.BeginAsync();

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync("FW.USP_CODE_SAVE", new
        {
            P_GROUP_CD = "FUNC_CD",
            P_ROWS = Tvp.From("FW.TVP_CODE", [new CodeRow { Code = "READ", CodeNm = "중복" }]),
        }, TestContext.Current.CancellationToken));

        Assert.Equal(MessageKeys.DupCode, ex.MessageKey);
    }

    [Theory]
    [InlineData("USER_STAT", "I")] // 값 추가 불가 시스템 그룹
    [InlineData("FUNC_CD", "D")]   // 값 추가는 되지만 삭제는 불가
    public async Task Save_시스템코드추가삭제_SYSTEM_CODE오류(string groupCd, string rowStat)
    {
        await using var db = await fixture.BeginAsync();

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync("FW.USP_CODE_SAVE", new
        {
            P_GROUP_CD = groupCd,
            P_ROWS = Tvp.From("FW.TVP_CODE", [new CodeRow { RowStat = rowStat, Code = rowStat == "I" ? "NEW_ONE" : "READ", RowVer = new byte[8] }]),
        }, TestContext.Current.CancellationToken));

        Assert.Equal(MessageKeys.SystemCode, ex.MessageKey);
    }

    [Fact]
    public async Task Save_사용자정의기능코드추가_허용()
    {
        await using var db = await fixture.BeginAsync();

        var saved = await db.Sp.ExecuteAsync("FW.USP_CODE_SAVE", new
        {
            P_GROUP_CD = "FUNC_CD",
            P_ROWS = Tvp.From("FW.TVP_CODE", [new CodeRow { Code = "CLOSE_MONTH", CodeNm = "월마감" }]),
        }, TestContext.Current.CancellationToken);

        Assert.Equal(1, saved.AffectedCnt);
    }

    public sealed class GroupDto
    {
        public string GroupCd { get; set; } = "";

        public string GroupNm { get; set; } = "";

        public string SystemYn { get; set; } = "";

        public string AllowAddYn { get; set; } = "";

        public int CodeCnt { get; set; }
    }

    /// <summary>FW.TVP_CODE_LANG 컬럼 순서.</summary>
    public sealed class NameRow
    {
        public string Code { get; set; } = "";

        public string LangCd { get; set; } = "";

        public string? CodeNm { get; set; }
    }

    [Fact]
    public async Task 그룹목록_시스템여부_코드수_사용자언어명칭()
    {
        await using var db = await fixture.BeginAsync();
        db.User.LangCd = "en";

        var page = await db.Sp.QueryPageAsync<GroupDto>("FW.USP_CODE_GROUP_GET_LIST",
            new { P_KEYWORD = "USER_STAT", P_PAGE_NO = 1, P_PAGE_SIZE = 50 }, TestContext.Current.CancellationToken);

        var group = Assert.Single(page.Items);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(("Y", "N", 6), (group.SystemYn, group.AllowAddYn, group.CodeCnt));
        Assert.Equal(await db.ScalarAsync<string>("SELECT GROUP_NM FROM FW.TB_CODE_GROUP_LANG WHERE GROUP_CD = 'USER_STAT' AND LANG_CD = 'en'"), group.GroupNm);
    }

    [Fact]
    public async Task 그룹목록_그룹명일부로검색_전체건수()
    {
        await using var db = await fixture.BeginAsync();
        var name = await db.ScalarAsync<string>("SELECT GROUP_NM FROM FW.TB_CODE_GROUP_LANG WHERE GROUP_CD = 'FUNC_CD' AND LANG_CD = 'ko'");

        var page = await db.Sp.QueryPageAsync<GroupDto>("FW.USP_CODE_GROUP_GET_LIST",
            new { P_KEYWORD = name[1..], P_PAGE_NO = 1, P_PAGE_SIZE = 50 }, TestContext.Current.CancellationToken);

        Assert.Contains(page.Items, g => g.GroupCd == "FUNC_CD");
        Assert.Equal(page.Items.Count, page.TotalCount);
    }

    [Fact]
    public async Task 언어별명칭목록_사용중인언어만()
    {
        await using var db = await fixture.BeginAsync();
        await db.ExecAsync("UPDATE FW.TB_LANG SET USE_YN = 'N' WHERE LANG_CD = 'zh-CN'");

        var names = await db.Sp.QueryAsync<NameRow>("FW.USP_CODE_LANG_GET_LIST", new { P_GROUP_CD = "USER_STAT" }, TestContext.Current.CancellationToken);

        Assert.Equal(["ko", "en"], names.Where(n => n.Code == "LOCKED").Select(n => n.LangCd));
    }

    [Fact]
    public async Task Save_언어별명칭_등록후수정과삭제_감사로그에명칭()
    {
        await using var db = await fixture.BeginAsync();
        await NewGroupAsync(db, "IT_GROUP");
        await db.Sp.ExecuteAsync("FW.USP_CODE_SAVE", new
        {
            P_GROUP_CD = "IT_GROUP",
            P_ROWS = Tvp.From("FW.TVP_CODE", [new CodeRow { Code = "A" }]),
            P_NAMES = Tvp.From("FW.TVP_CODE_LANG", [
                new NameRow { Code = "A", LangCd = "ko", CodeNm = "에이" },
                new NameRow { Code = "A", LangCd = "en", CodeNm = "A" },
                new NameRow { Code = "A", LangCd = "zh-CN", CodeNm = "甲" }]),
        }, TestContext.Current.CancellationToken);
        var a = (await List(db, "IT_GROUP")).Single();

        await db.Sp.ExecuteAsync("FW.USP_CODE_SAVE", new
        {
            P_GROUP_CD = "IT_GROUP",
            P_ROWS = Tvp.From("FW.TVP_CODE", [new CodeRow { RowStat = "U", Code = "A", RowVer = a.RowVer }]),
            P_NAMES = Tvp.From("FW.TVP_CODE_LANG", [
                new NameRow { Code = "A", LangCd = "ko", CodeNm = " 에이2 " },
                new NameRow { Code = "A", LangCd = "en", CodeNm = "" }]),
        }, TestContext.Current.CancellationToken);

        var names = await db.Sp.QueryAsync<NameRow>("FW.USP_CODE_LANG_GET_LIST", new { P_GROUP_CD = "IT_GROUP" }, TestContext.Current.CancellationToken);
        Assert.Equal([("ko", "에이2"), ("zh-CN", "甲")], names.Select(n => (n.LangCd, n.CodeNm)));
        var after = await db.ScalarAsync<string>("SELECT TOP (1) AFTER_JSON FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_CODE' AND ROW_KEY = 'IT_GROUP' ORDER BY LOG_ID DESC");
        Assert.Contains("\"NAMES\":[", after, StringComparison.Ordinal);
        Assert.Contains("{\"LANG_CD\":\"ko\",\"CODE_NM\":\"에이2\"}", after, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ja", "A", "あ")]  // 사용하지 않는 언어
    [InlineData("en", "B", "B")]   // 저장 행에 없는 코드
    [InlineData("ko", "A", " ")]   // 기본 언어 명칭은 비울 수 없다
    public async Task Save_명칭이잘못되면_INVALID_INPUT(string langCd, string code, string name)
    {
        await using var db = await fixture.BeginAsync();
        await NewGroupAsync(db, "IT_GROUP");

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync("FW.USP_CODE_SAVE", new
        {
            P_GROUP_CD = "IT_GROUP",
            P_ROWS = Tvp.From("FW.TVP_CODE", [new CodeRow { Code = "A" }]),
            P_NAMES = Tvp.From("FW.TVP_CODE_LANG", [new NameRow { Code = code, LangCd = langCd, CodeNm = name }]),
        }, TestContext.Current.CancellationToken));

        Assert.Equal(MessageKeys.InvalidInput, ex.MessageKey);
    }

    private static Task<int> NewGroupAsync(TestDb db, string groupCd) =>
        db.ExecAsync("INSERT INTO FW.TB_CODE_GROUP (GROUP_CD, GROUP_NM) VALUES (@G, @G)", ("@G", groupCd));

    private static async Task<IReadOnlyList<CodeDto>> List(TestDb db, string groupCd) =>
        (await db.Sp.QueryPageAsync<CodeDto>("FW.USP_CODE_GET_LIST", new { P_GROUP_CD = groupCd, P_PAGE_NO = 1, P_PAGE_SIZE = 50 }, TestContext.Current.CancellationToken)).Items;
}
