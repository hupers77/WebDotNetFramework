using System.Text.Json;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Data;
using Portal.Infrastructure.Excel;
using Portal.Testing;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.UnitTests.Web;

/// <summary>다국어 문구 관리 Service: 키 형식, 종류, 기본 언어 필수, 쓰지 않는 언어 거절, 키 단위 모으기, 엑셀 내보내기·업로드 미리보기.</summary>
public sealed class LangAdminServiceTests : IAsyncLifetime
{
    private static readonly byte[] KeyVer = [0, 0, 0, 0, 0, 0, 0, 5];
    private readonly FakeSpExecutor _sp = new();
    private LangAdminService _service = null!;

    public async ValueTask InitializeAsync()
    {
        _sp.Returns(LangAdminService.SaveSp, new SaveResult(1, null));
        _service = new LangAdminService(_sp, await NewScreensTestSupport.LangsAsync(_sp), new NewScreensTestSupport.KeyLocalizer());
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LangSaveRow Row(string rowStat = "I", string key = "SYS.TEST.TITLE", string type = "LABEL") =>
        new() { RowStat = rowStat, ResKey = key, ResType = type, Texts = new() { ["ko"] = " 제목 ", ["en"] = "Title" }, RowVer = rowStat == "I" ? null : KeyVer };

    private static LangSaveRequest Request(params LangSaveRow[] rows) => new() { Rows = [.. rows] };

    private void ReturnsList(params LangResourceRow[] rows) =>
        _sp.Returns(LangAdminService.ListSp, new object?[] { rows.ToList(), null }.Select(x => x!).ToArray());

    private static LangResourceRow Text(string key, string lang, string text, string type = "LABEL") =>
        new() { ResKey = key, LangCd = lang, ResText = text, ResType = type, KeyVer = KeyVer };

    [Fact]
    public async Task SaveAsync_문구_공백을_빼고_RowVer를_16진수로_넘긴다()
    {
        await _service.SaveAsync(Request(Row(), Row("U", "SYS.TEST.OTHER")), Ct);

        var json = JsonDocument.Parse((string)_sp.LastCall(LangAdminService.SaveSp).Parameters["P_ROWS_JSON"]!).RootElement;
        Assert.Equal("제목", json[0].GetProperty("texts")[0].GetProperty("resText").GetString());
        Assert.Equal(JsonValueKind.Null, json[0].GetProperty("rowVer").ValueKind);
        Assert.Equal("0000000000000005", json[1].GetProperty("rowVer").GetString());
    }

    [Fact]
    public async Task SaveAsync_삭제는_문구없이_RowVer만_있으면_된다()
    {
        var row = Row("D");
        row.Texts = [];
        row.ResType = null;

        await _service.SaveAsync(Request(row), Ct);

        Assert.Single(_sp.Calls, c => c.SpName == LangAdminService.SaveSp);
    }

    [Theory]
    [InlineData("sys.test.title")]
    [InlineData("SYSTITLE")]
    [InlineData(".SYS.TITLE")]
    [InlineData("SYS..TITLE")]
    [InlineData("SYS.TITLE.")]
    [InlineData("SYS.TI TLE")]
    public async Task SaveAsync_키_형식이_틀리면_거절한다(string key)
    {
        var ex = await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(Row(key: key)), Ct));

        Assert.Equal(MessageKeys.InvalidInput, ex.MessageKey);
    }

    [Fact]
    public async Task SaveAsync_기본언어_없음_알수없는_종류_쓰지않는_언어를_거절한다()
    {
        var noDefault = Row();
        noDefault.Texts = new() { ["en"] = "Title" };
        var badType = Row(type: "HTML");
        var badLang = Row();
        badLang.Texts["zh-CN"] = "标题";

        foreach (var row in new[] { noDefault, badType, badLang })
        {
            await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(row), Ct));
        }

        Assert.DoesNotContain(_sp.Calls, c => c.SpName == LangAdminService.SaveSp);
    }

    [Fact]
    public async Task GetListAsync_키_하나에_언어별_문구를_모은다()
    {
        ReturnsList(Text("A.B", "ko", "가"), Text("A.B", "en", "a"), Text("C.D", "ko", "나", "MSG"));

        var page = await _service.GetListAsync(new LangSearch { Keyword = " 가 ", MissingYn = "Y" }, Ct);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal("a", page.Items[0].Texts["en"]);
        Assert.Equal("MSG", page.Items[1].ResType);
        var args = _sp.LastCall(LangAdminService.ListSp).Parameters;
        Assert.Equal("가", args["P_KEYWORD"]);
        Assert.Equal("Y", args["P_MISSING_YN"]);
    }

    [Fact]
    public async Task ExportAsync_엑셀을_만든다()
    {
        ReturnsList(Text("A.B", "ko", "가"), Text("A.B", "en", "a"));

        await using var stream = await _service.ExportAsync(new LangSearch(), Ct);
        var table = ExcelReader.Read(stream, 100);

        Assert.Equal(["KEY", "TYPE", "ko", "en"], table.Headers);
        Assert.Equal("A.B", table.Rows[0][0]);
        Assert.Equal("a", table.Rows[0][3]);
    }

    [Fact]
    public async Task PreviewUploadAsync_추가_수정_변화없음_오류를_나눈다()
    {
        ReturnsList(Text("A.B", "ko", "가"), Text("A.B", "en", "a"), Text("C.D", "ko", "나"));
        var sheet = new ExcelSheet<string[]>("LANG",
            [new("KEY", r => r[0]), new("TYPE", r => r[1]), new("ko", r => r[2]), new("en", r => r[3])],
            [
                ["A.B", "LABEL", "가", "a"],     // 변화 없음
                ["C.D", "LABEL", "나2", ""],     // 수정
                ["E.F", "MSG", "새", "new"],     // 추가
                ["bad key", "LABEL", "x", "y"],  // 키 오류
                ["G.H", "LABEL", "", "only"],    // 기본 언어 없음
            ]);
        await using var xlsx = ExcelWriter.Write(sheet);

        var result = await _service.PreviewUploadAsync(xlsx, Ct);

        Assert.Equal(1, result.UnchangedCnt);
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal(["C.D", "E.F"], result.Rows.Select(r => r.ResKey));
        Assert.Equal("U", result.Rows[0].RowStat);
        Assert.Equal(KeyVer, result.Rows[0].RowVer);
        Assert.Equal("I", result.Rows[1].RowStat);
    }

    [Fact]
    public async Task PreviewUploadAsync_머리글이_다르면_거절한다()
    {
        ReturnsList();
        var sheet = new ExcelSheet<string[]>("LANG", [new("KEY", r => r[0]), new("TYPE", r => r[1]), new("fr", r => r[2])], [["A.B", "LABEL", "x"]]);
        await using var xlsx = ExcelWriter.Write(sheet);

        var ex = await Assert.ThrowsAsync<BizException>(() => _service.PreviewUploadAsync(xlsx, Ct));

        Assert.Equal(ExcelMessageKeys.HeaderMismatch, ex.MessageKey);
    }
}
