using Microsoft.Extensions.DependencyInjection;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Localization;
using Portal.Core.Results;
using Portal.Data;
using Portal.Infrastructure.Localization;
using Portal.Testing;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.UnitTests.Sys;

/// <summary>공통코드 관리 Service의 입력 규칙 (화면 검증과 같은 규칙을 서버에서 다시 확인).</summary>
public sealed class CodeAdminServiceTests
{
    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 1];
    private readonly FakeSpExecutor _sp = new();
    private readonly CodeAdminService _service;

    public CodeAdminServiceTests()
    {
        _sp.Returns(LangResources.ListSp, new List<LangResources.ResourceRow>());
        _sp.Returns(LangResources.LanguageSp, new List<LangResources.LanguageRow>
        {
            new() { LangCd = "ko", LangNm = "한국어", DefaultYn = "Y" },
            new() { LangCd = "en", LangNm = "English" },
        });
        var langs = new LangResources(new ServiceCollection().AddSingleton<ISpExecutor>(_sp).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
        langs.ReloadAsync(CancellationToken.None).GetAwaiter().GetResult();
        _service = new CodeAdminService(_sp, langs, new KeyLocalizer());
    }

    private static CodeSaveRequest Request(params CodeSaveRow[] rows) => new() { GroupCd = "G", Rows = [.. rows] };

    private static CodeSaveRow Insert(string code = "A", string? ko = "가") =>
        new() { RowStat = "I", Code = code, Names = ko is null ? [] : new() { ["ko"] = ko } };

    private string ErrorOf(CodeSaveRequest request) => Assert.Throws<BizException>(() => _service.Validate(request)).MessageKey;

    [Fact]
    public async Task SaveAsync_추가행_코드와언어별명칭을TVP로보낸다()
    {
        _sp.Returns(CodeAdminService.SaveSp, new SaveResult(1, null));
        var row = Insert(" NEW_1 ");
        row.Names["en"] = " New ";
        row.SortNo = 20;

        var result = await _service.SaveAsync(Request(row), TestContext.Current.CancellationToken);

        Assert.Equal(1, result.AffectedCnt);
        var call = _sp.LastCall(CodeAdminService.SaveSp);
        Assert.Equal("G", call.Parameters["P_GROUP_CD"]);
        var rows = ((Tvp)call.Parameters["P_ROWS"]!).Table;
        Assert.Equal("NEW_1", rows.Rows[0]["CODE"]);
        Assert.Equal(DBNull.Value, rows.Rows[0]["CODE_NM"]);
        var names = ((Tvp)call.Parameters["P_NAMES"]!).Table;
        Assert.Equal([("ko", "가"), ("en", "New")], names.Rows.Cast<System.Data.DataRow>().Select(r => ((string)r["LANG_CD"], (string)r["CODE_NM"])));
    }

    [Fact]
    public void 변경행없음_NO_CHANGES() => Assert.Equal(MessageKeys.NoChanges, ErrorOf(Request()));

    [Theory]
    [InlineData("A B")]
    [InlineData("가나")]
    [InlineData("ABCDEFGHIJ_ABCDEFGHIJ_ABCDEFGHIJ")]
    public void 추가행_코드형식오류(string code) => Assert.Equal("VAL.CODE_FORMAT", ErrorOf(Request(Insert(code))));

    [Fact]
    public void 추가행_기본언어명칭없음_필수오류() => Assert.Equal("VAL.REQUIRED", ErrorOf(Request(Insert(ko: "  "))));

    [Fact]
    public void 수정행_기본언어명칭을비우면_필수오류()
    {
        var row = new CodeSaveRow { RowStat = "U", Code = "A", RowVer = RowVer, Names = new() { ["ko"] = "" } };

        Assert.Equal("VAL.REQUIRED", ErrorOf(Request(row)));
    }

    [Fact]
    public void 수정행_다른언어만비우면_통과_명칭삭제로보낸다()
    {
        var row = new CodeSaveRow { RowStat = "U", Code = "A", RowVer = RowVer, Names = new() { ["en"] = "" } };

        var (_, names) = _service.Validate(Request(row));

        Assert.Equal("", Assert.Single(names).CodeNm);
    }

    [Fact]
    public void 같은코드두번_중복행오류() => Assert.Equal("VAL.DUP_ROW", ErrorOf(Request(Insert("A"), Insert("a"))));

    [Fact]
    public void 사용하지않는언어_입력오류()
    {
        var row = Insert();
        row.Names["ja"] = "あ";

        Assert.Equal(MessageKeys.InvalidInput, ErrorOf(Request(row)));
    }

    [Theory]
    [InlineData("X", true)]
    [InlineData("U", false)]
    [InlineData("D", false)]
    public void 행상태오류_또는_수정삭제행에ROW_VER없음_입력오류(string rowStat, bool withRowVer)
    {
        var row = new CodeSaveRow { RowStat = rowStat, Code = "A", RowVer = withRowVer ? RowVer : null };

        Assert.Equal(MessageKeys.InvalidInput, ErrorOf(Request(row)));
    }

    [Fact]
    public void 종료일이시작일보다앞_기간오류()
    {
        var row = Insert();
        row.StartDt = new DateOnly(2026, 2, 1);
        row.EndDt = new DateOnly(2026, 1, 31);

        Assert.Equal("VAL.DATE_RANGE", ErrorOf(Request(row)));
    }

    [Fact]
    public void 속성길이초과_최대길이오류()
    {
        var row = Insert();
        row.Attr3 = new string('x', 201);

        Assert.Equal("VAL.MAX_LENGTH", ErrorOf(Request(row)));
    }

    [Fact]
    public void 삭제행은_명칭과값을검사하지않는다()
    {
        var row = new CodeSaveRow { RowStat = "D", Code = "A", RowVer = RowVer, Attr1 = new string('x', 300), Names = new() { ["ja"] = "x" } };

        var (rows, names) = _service.Validate(Request(row));

        Assert.Equal("D", Assert.Single(rows).RowStat);
        Assert.Empty(names);
    }

    [Fact]
    public async Task GetCodesAsync_언어별명칭을코드에붙인다()
    {
        _sp.Returns(CodeAdminService.ListSp, new PageResult<CodeDto>
        {
            Items = [new() { Code = "A", CodeNm = "가" }, new() { Code = "B", CodeNm = "나" }],
            TotalCount = 2,
            PageNo = 1,
            PageSize = CodeAdminService.MaxRows,
        });
        _sp.Returns(CodeAdminService.NameListSp, new List<CodeAdminService.NameRow>
        {
            new() { Code = "A", LangCd = "ko", CodeNm = "가" },
            new() { Code = "A", LangCd = "en", CodeNm = "Ga" },
        });

        var list = await _service.GetCodesAsync("G", TestContext.Current.CancellationToken);

        Assert.Equal("Ga", list.Codes.Items[0].Names["en"]);
        Assert.Empty(list.Codes.Items[1].Names);
        Assert.Equal(CodeAdminService.MaxRows, _sp.LastCall(CodeAdminService.ListSp).Parameters["P_PAGE_SIZE"]);
    }

    /// <summary>번역 대신 키를 그대로 돌려준다.</summary>
    private sealed class KeyLocalizer : IPortalLocalizer
    {
        public string this[string key] => key;

        public string this[string key, params object?[] args] => key;

        public string Get(string langCd, string key, params object?[] args) => key;
    }
}
