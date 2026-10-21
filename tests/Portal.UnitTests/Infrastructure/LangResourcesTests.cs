using Microsoft.Extensions.DependencyInjection;
using Portal.Data;
using Portal.Infrastructure.Localization;
using Portal.Testing;

namespace Portal.UnitTests.Infrastructure;

public sealed class LangResourcesTests
{
    private readonly FakeSpExecutor _sp = new();
    private readonly LangResources _texts;

    public LangResourcesTests()
    {
        _sp.Returns(LangResources.ListSp, new List<LangResources.ResourceRow>
        {
            new() { LangCd = "ko", ResKey = "BTN.SAVE", ResText = "저장" },
            new() { LangCd = "en", ResKey = "BTN.SAVE", ResText = "Save" },
            new() { LangCd = "ko", ResKey = "LBL.TOTAL", ResText = "전체 {0:N0}건" },
            new() { LangCd = "ko", ResKey = "LBL.BROKEN", ResText = "깨진 {형식" },
        });
        _sp.Returns(LangResources.LanguageSp, new List<LangResources.LanguageRow>
        {
            new() { LangCd = "ko", LangNm = "한국어", DefaultYn = "Y" },
            new() { LangCd = "en", LangNm = "English" },
        });
        var services = new ServiceCollection().AddSingleton<ISpExecutor>(_sp).BuildServiceProvider();
        _texts = new LangResources(services.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task Get_사용자언어문구()
    {
        await _texts.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Save", _texts.Get("en", "BTN.SAVE", []));
        Assert.Equal("저장", _texts.Get("ko", "BTN.SAVE", []));
    }

    [Fact]
    public async Task Get_사용자언어문구없음_기본언어_그래도없으면키()
    {
        await _texts.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("저장", _texts.Get("zh-CN", "BTN.SAVE", []));
        Assert.Equal("NO.SUCH_KEY", _texts.Get("en", "NO.SUCH_KEY", []));
    }

    [Fact]
    public async Task Get_인자_언어형식으로채움_형식오류면원문()
    {
        await _texts.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("전체 1,234건", _texts.Get("ko", "LBL.TOTAL", [1234]));
        Assert.Equal("깨진 {형식", _texts.Get("ko", "LBL.BROKEN", [1]));
    }

    [Fact]
    public async Task Reload_통째로교체()
    {
        await _texts.ReloadAsync(TestContext.Current.CancellationToken);
        _sp.Returns(LangResources.ListSp, new List<LangResources.ResourceRow> { new() { LangCd = "ko", ResKey = "BTN.SAVE", ResText = "보관" } });

        await _texts.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("보관", _texts.Get("ko", "BTN.SAVE", []));
        Assert.Equal("보관", _texts.Get("en", "BTN.SAVE", []));
    }

    [Fact]
    public async Task GetAll_사용자언어로채우고없는키는기본언어()
    {
        await _texts.ReloadAsync(TestContext.Current.CancellationToken);

        var en = _texts.GetAll("en");

        Assert.Equal("Save", en["BTN.SAVE"]);
        Assert.Equal("전체 {0:N0}건", en["LBL.TOTAL"]);
        Assert.Equal(["ko", "en"], _texts.Languages.Select(l => l.LangCd));
        Assert.True(_texts.Languages[0].IsDefault);
    }

    [Fact]
    public async Task Version_내용이같으면같고바뀌면달라짐()
    {
        await _texts.ReloadAsync(TestContext.Current.CancellationToken);
        var first = _texts.Version;
        await _texts.ReloadAsync(TestContext.Current.CancellationToken);
        var same = _texts.Version;
        _sp.Returns(LangResources.ListSp, new List<LangResources.ResourceRow> { new() { LangCd = "ko", ResKey = "BTN.SAVE", ResText = "보관" } });
        await _texts.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(first, same);
        Assert.NotEqual(first, _texts.Version);
    }
}
