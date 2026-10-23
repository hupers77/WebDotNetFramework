using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Portal.Core.Errors;
using Portal.Core.Results;
using Portal.Data;
using Portal.Infrastructure.Localization;
using Portal.Testing;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.UnitTests.Web;

/// <summary>공지 팝업 Service의 입력 규칙: 사용 중인 언어만, 기본 언어 필수, 제목·내용 필수, 수정은 RowVer 필수이고 문구는 JSON으로 SP에 넘긴다.</summary>
public sealed class PopupAdminServiceTests : IAsyncLifetime
{
    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 0x4D];
    private readonly FakeSpExecutor _sp = new();
    private PopupAdminService _service = null!;

    public PopupAdminServiceTests()
    {
        _sp.Returns(PopupAdminService.SaveSp, new SaveResult(1, "77"));
        _sp.Returns(PopupAdminService.DeleteSp, new SaveResult(1, null));
        _sp.Returns(LangResources.ListSp, new List<LangResources.ResourceRow>());
        _sp.Returns(LangResources.LanguageSp, new List<LangResources.LanguageRow>
        {
            new() { LangCd = "ko", LangNm = "한국어", DefaultYn = "Y" },
            new() { LangCd = "en", LangNm = "English" },
        });
    }

    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection().AddSingleton<ISpExecutor>(_sp).BuildServiceProvider();
        var langs = new LangResources(services.GetRequiredService<IServiceScopeFactory>());
        await langs.ReloadAsync(TestContext.Current.CancellationToken);
        _service = new PopupAdminService(_sp, langs);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PopupSaveRequest Request(params PopupLangDto[] langs) => new()
    {
        OpenScopeCd = "ALL", StartDt = new DateOnly(2026, 11, 1), EndDt = new DateOnly(2026, 11, 30), UseYn = "Y",
        Langs = langs.Length == 0 ? [Text("ko", " 점검 안내 ", " 내용 ")] : [.. langs],
    };

    private static PopupLangDto Text(string lang, string title, string content) => new() { LangCd = lang, Title = title, Content = content };

    [Fact]
    public async Task SaveAsync_등록은_ID없이_문구를_공백없이_JSON으로_넘기고_새_ID를_돌려준다()
    {
        var result = await _service.SaveAsync(Request(), Ct);

        var call = _sp.LastCall(PopupAdminService.SaveSp).Parameters;
        var langs = JsonDocument.Parse((string)call["P_LANGS_JSON"]!).RootElement;
        Assert.Equal(77, result.PopupId);
        Assert.Null(call["P_POPUP_ID"]);
        Assert.Equal("점검 안내", langs[0].GetProperty("title").GetString());
        Assert.Equal("내용", langs[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task SaveAsync_수정은_ID와_RowVer를_넘긴다()
    {
        var request = Request(Text("ko", "제목", "내용"), Text("en", "Title", "Body"));
        request.PopupId = 5;
        request.RowVer = RowVer;

        await _service.SaveAsync(request, Ct);

        var call = _sp.LastCall(PopupAdminService.SaveSp).Parameters;
        Assert.Equal(5L, call["P_POPUP_ID"]);
        Assert.Equal(RowVer, call["P_ROW_VER"]);
    }

    [Fact]
    public async Task SaveAsync_기본언어가_없거나_모르는_언어거나_중복이거나_빈_문구면_거절()
    {
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(Text("en", "T", "B")), Ct));
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(Text("ko", "T", "B"), Text("fr", "T", "B")), Ct));
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(Text("ko", "T", "B"), Text("ko", "T2", "B2")), Ct));
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(Text("ko", "  ", "B")), Ct));
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(Text("ko", "T", " ")), Ct));

        Assert.DoesNotContain(_sp.Calls, c => c.SpName == PopupAdminService.SaveSp);
    }

    [Fact]
    public async Task GetListAsync_검색어는_공백을_빼고_빈_조건은_null로_넘긴다()
    {
        _sp.Returns(PopupAdminService.ListSp, new PageResult<PopupDto> { Items = [], TotalCount = 0, PageNo = 1, PageSize = 20 });

        await _service.GetListAsync(new PopupSearch { Keyword = "  점검 ", UseYn = "" }, Ct);

        var call = _sp.LastCall(PopupAdminService.ListSp).Parameters;
        Assert.Equal("점검", call["P_KEYWORD"]);
        Assert.Null(call["P_USE_YN"]);
    }

    [Fact]
    public void PopupDto_언어별_문구_JSON을_목록으로_바꾼다()
    {
        var dto = new PopupDto { LangsJson = "[{\"langCd\":\"ko\",\"title\":\"가\",\"content\":\"나\"}]" };

        Assert.Equal("가", dto.Langs.Single().Title);
        Assert.Empty(new PopupDto().Langs);
    }
}
