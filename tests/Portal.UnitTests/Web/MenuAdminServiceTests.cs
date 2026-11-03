using System.Text.Json;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Data;
using Portal.Testing;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.UnitTests.Web;

/// <summary>메뉴 관리 Service: 세트·유형별 규칙(폴더/화면/링크), 키 형식, 기본 언어 메뉴명, 링크 안전성을 SP 호출 전에 거절하고 SP에는 JSON으로 넘긴다.</summary>
public sealed class MenuAdminServiceTests : IAsyncLifetime
{
    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 7];
    private readonly FakeSpExecutor _sp = new();
    private MenuAdminService _service = null!;

    public async ValueTask InitializeAsync()
    {
        _sp.Returns(MenuAdminService.SaveSp, new SaveResult(2, null));
        _service = new MenuAdminService(_sp, await NewScreensTestSupport.LangsAsync(_sp));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MenuSaveRow Row(string rowStat = "I", string key = "NEW_MENU", string type = "PAGE", string? program = "SYS_USER_MGT", string? link = null, string? parent = "SYS") =>
        new()
        {
            RowStat = rowStat, MenuKey = key, MenuTypeCd = type, ProgramId = program, LinkUrl = link, ParentMenuKey = parent,
            Names = new() { ["ko"] = " 새 메뉴 ", ["en"] = "New" },
            RowVer = rowStat == "I" ? null : RowVer,
        };

    private static MenuSaveRequest Request(params MenuSaveRow[] rows) => new() { MenuSetCd = "PORTAL", Rows = [.. rows] };

    [Fact]
    public async Task SaveAsync_추가와수정은_이름공백을_빼고_RowVer를_16진수로_넘긴다()
    {
        var result = await _service.SaveAsync(Request(Row(), Row("U", "OLD_MENU")), Ct);

        var call = _sp.LastCall(MenuAdminService.SaveSp).Parameters;
        var json = JsonDocument.Parse((string)call["P_ROWS_JSON"]!).RootElement;
        Assert.Equal(2, result.AffectedCnt);
        Assert.Equal("PORTAL", call["P_MENU_SET_CD"]);
        Assert.Equal("새 메뉴", json[0].GetProperty("names")[0].GetProperty("menuNm").GetString());
        Assert.Equal(JsonValueKind.Null, json[0].GetProperty("rowVer").ValueKind);
        Assert.Equal("0000000000000007", json[1].GetProperty("rowVer").GetString());
    }

    [Fact]
    public async Task SaveAsync_삭제는_이름없이_RowVer만_있으면_된다()
    {
        var row = Row("D");
        row.Names = [];
        row.MenuTypeCd = null;

        await _service.SaveAsync(Request(row), Ct);

        Assert.Single(_sp.Calls, c => c.SpName == MenuAdminService.SaveSp);
    }

    [Fact]
    public async Task SaveAsync_빈_요청은_변경없음()
    {
        var ex = await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(), Ct));

        Assert.Equal(MessageKeys.NoChanges, ex.MessageKey);
    }

    public static TheoryData<string> BadRows => new()
    {
        "type",        // 알 수 없는 유형
        "folder-prog", // 폴더에 프로그램
        "page-noprog", // 화면에 프로그램 없음
        "page-link",   // 화면에 링크
        "link-prog",   // 링크에 프로그램
        "link-js",     // javascript: 주소
        "link-proto",  // //evil.com
        "link-space",  // 공백이 든 주소
        "key",         // 키에 공백
        "self-parent", // 자기 자신이 부모
        "no-default",  // 기본 언어 이름 없음
        "unknown-lang",// 쓰지 않는 언어
        "scope",       // 알 수 없는 공개 대상
        "no-ver",      // 수정인데 RowVer 없음
    };

    [Theory]
    [MemberData(nameof(BadRows))]
    public async Task SaveAsync_잘못된_행은_SP를_부르지_않고_거절한다(string kind)
    {
        var row = kind switch
        {
            "type" => Row(type: "WIDGET"),
            "folder-prog" => Row(type: "FOLDER"),
            "page-noprog" => Row(program: null),
            "page-link" => Row(link: "/x"),
            "link-prog" => Row(type: "LINK", link: "/x"),
            "link-js" => Row(type: "LINK", program: null, link: "javascript:alert(1)"),
            "link-proto" => Row(type: "LINK", program: null, link: "//evil.example"),
            "link-space" => Row(type: "LINK", program: null, link: "/a b"),
            "key" => Row(key: "BAD KEY"),
            "self-parent" => Row(parent: "NEW_MENU"),
            "no-default" => Row(),
            "unknown-lang" => Row(),
            "scope" => Row(),
            _ => Row("U"),
        };
        switch (kind)
        {
            case "no-default": row.Names = new() { ["en"] = "New" }; break;
            case "unknown-lang": row.Names["zh-CN"] = "新"; break;
            case "scope": row.OpenScopeCd = "ALL;DROP"; break;
            case "no-ver": row.RowVer = null; break;
        }

        var ex = await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(row), Ct));

        Assert.Equal(MessageKeys.InvalidInput, ex.MessageKey);
        Assert.DoesNotContain(_sp.Calls, c => c.SpName == MenuAdminService.SaveSp);
    }

    [Fact]
    public async Task SaveAsync_같은_키_중복과_알수없는_세트는_거절한다()
    {
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(Row(key: "A"), Row(key: "A")), Ct));
        var bad = Request(Row());
        bad.MenuSetCd = "OTHER";
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(bad, Ct));
    }

    [Fact]
    public async Task SaveAsync_내부경로와_https_링크는_허용한다()
    {
        await _service.SaveAsync(Request(Row(key: "L1", type: "LINK", program: null, link: "/Board/Notice"), Row(key: "L2", type: "LINK", program: null, link: "https://example.com/a")), Ct);

        Assert.Single(_sp.Calls, c => c.SpName == MenuAdminService.SaveSp);
    }

    [Fact]
    public async Task GetListAsync_메뉴명을_메뉴_키별로_모은다()
    {
        _sp.Returns(MenuAdminService.ListSp, new object?[]
        {
            new List<MenuAdminDto> { new() { MenuKey = "A", MenuTypeCd = "FOLDER", Depth = 1 } },
            new List<MenuNameRow> { new() { MenuKey = "A", LangCd = "ko", MenuNm = "가" }, new() { MenuKey = "A", LangCd = "en", MenuNm = "A" } },
            new List<MenuProgramOption> { new() { ProgramId = "P" } },
            null,
        }.Select(x => x!).ToArray());

        var result = await _service.GetListAsync("PORTAL", Ct);

        Assert.Equal("가", result.Items[0].Names["ko"]);
        Assert.Equal("A", result.Items[0].Names["en"]);
        Assert.Single(result.Programs);
        await Assert.ThrowsAsync<BizException>(() => _service.GetListAsync("X", Ct));
    }
}
