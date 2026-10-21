using System.Collections.Frozen;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Portal.Core.Keys;
using Portal.Data;
using Portal.Infrastructure.Caching;

namespace Portal.Infrastructure.Localization;

/// <summary>
/// 다국어 문구 캐시. <c>FW.TB_LANG_RESOURCE</c> 전체를 메모리에 두고 캐시 버전 <c>LANG</c>이 바뀌면 통째로 바꾼다.
/// 없는 문구는 기본 언어(<see cref="DefaultLang"/>), 그래도 없으면 키를 그대로 보여 준다(빠진 문구가 화면에서 바로 보이도록).
/// </summary>
public sealed class LangResources(IServiceScopeFactory scopeFactory) : ICacheVersionListener
{
    /// <summary>기본 언어 (초기 데이터 <c>TB_LANG.DEFAULT_YN</c>).</summary>
    public const string DefaultLang = "ko";

    internal const string ListSp = "FW.USP_LANG_RESOURCE_GET_LIST";

    private volatile FrozenDictionary<(string Lang, string Key), string> _texts =
        FrozenDictionary<(string Lang, string Key), string>.Empty;

    /// <inheritdoc />
    public string CacheKey => CacheKeys.Lang;

    /// <summary>문구를 찾는다. 인자가 있으면 <c>{0}</c> 형식으로 넣는다(형식이 잘못된 문구는 인자 없이 그대로).</summary>
    public string Get(string langCd, string key, IReadOnlyList<object?> args)
    {
        var texts = _texts;
        var text = texts.GetValueOrDefault((langCd, key))
            ?? texts.GetValueOrDefault((DefaultLang, key))
            ?? key;
        if (args.Count == 0)
        {
            return text;
        }

        try
        {
            return string.Format(CultureInfo.GetCultureInfo(langCd), text, [.. args]);
        }
        catch (FormatException)
        {
            return text;
        }
    }

    /// <inheritdoc />
    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider.GetRequiredService<ISpExecutor>();
        var rows = await sp.QueryAsync<ResourceRow>(ListSp, null, cancellationToken);
        _texts = rows.ToFrozenDictionary(r => (r.LangCd, r.ResKey), r => r.ResText);
    }

    internal sealed class ResourceRow
    {
        public string ResKey { get; set; } = "";

        public string LangCd { get; set; } = "";

        public string ResText { get; set; } = "";
    }
}
