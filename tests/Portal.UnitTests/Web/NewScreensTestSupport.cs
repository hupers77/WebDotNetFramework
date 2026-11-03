using Microsoft.Extensions.DependencyInjection;
using Portal.Core.Localization;
using Portal.Data;
using Portal.Infrastructure.Localization;
using Portal.Testing;

namespace Portal.UnitTests.Web;

/// <summary>새 관리 화면 Service 테스트가 함께 쓰는 준비 코드 (언어 ko·en, 번역 대신 키를 돌려주는 번역기).</summary>
internal static class NewScreensTestSupport
{
    /// <summary>ko(기본)·en 두 언어를 가진 문구 캐시를 만든다.</summary>
    public static async Task<LangResources> LangsAsync(FakeSpExecutor sp)
    {
        sp.Returns(LangResources.ListSp, new List<LangResources.ResourceRow>());
        sp.Returns(LangResources.LanguageSp, new List<LangResources.LanguageRow>
        {
            new() { LangCd = "ko", LangNm = "한국어", DefaultYn = "Y" },
            new() { LangCd = "en", LangNm = "English" },
        });
        var services = new ServiceCollection().AddSingleton<ISpExecutor>(sp).BuildServiceProvider();
        var langs = new LangResources(services.GetRequiredService<IServiceScopeFactory>());
        await langs.ReloadAsync(TestContext.Current.CancellationToken);
        return langs;
    }

    /// <summary>번역 대신 키를 그대로 돌려준다 (인자가 있으면 "키:인자1|인자2").</summary>
    public sealed class KeyLocalizer : IPortalLocalizer
    {
        public string this[string key] => key;

        public string this[string key, params object?[] args] => args.Length == 0 ? key : $"{key}:{string.Join("|", args)}";

        public string Get(string langCd, string key, params object?[] args) => key;
    }
}
