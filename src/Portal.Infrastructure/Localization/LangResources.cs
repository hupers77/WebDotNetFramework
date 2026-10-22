using System.Collections.Frozen;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
    internal const string LanguageSp = "FW.USP_LANG_GET_LIST";

    private volatile Snapshot _snapshot = new(FrozenDictionary<(string Lang, string Key), string>.Empty, [], "0");

    /// <inheritdoc />
    public string CacheKey => CacheKeys.Lang;

    /// <summary>사용 중인 언어 (정렬 순서). 헤더 언어 전환에 쓴다.</summary>
    public IReadOnlyList<LanguageInfo> Languages => _snapshot.Languages;

    /// <summary>
    /// 문구 내용의 버전 (내용 해시). 화면용 다국어 묶음(<c>/i18n/ko.json?v=</c>)의 브라우저 캐시 키로 쓴다.
    /// 내용으로 만들므로 웹서버가 여러 대여도 같은 값이 나온다.
    /// </summary>
    public string Version => _snapshot.Version;

    /// <summary>한 언어의 문구 전체 (없는 키는 기본 언어로 채운다). 화면 JS의 <c>fw.t</c>용.</summary>
    public IReadOnlyDictionary<string, string> GetAll(string langCd)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var ((lang, key), text) in _snapshot.Texts)
        {
            if (lang == langCd || (lang == DefaultLang && !result.ContainsKey(key)))
            {
                result[key] = text;
            }
        }

        return result;
    }

    /// <summary>문구를 찾는다. 인자가 있으면 <c>{0}</c> 형식으로 넣는다(형식이 잘못된 문구는 인자 없이 그대로).</summary>
    public string Get(string langCd, string key, IReadOnlyList<object?> args)
    {
        var texts = _snapshot.Texts;
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
        var languages = await sp.QueryAsync<LanguageRow>(LanguageSp, null, cancellationToken);
        _snapshot = new(
            rows.ToFrozenDictionary(r => (r.LangCd, r.ResKey), r => r.ResText),
            [.. languages.Select(l => new LanguageInfo(l.LangCd, l.LangNm, l.DefaultYn == "Y"))],
            ContentVersion(rows));
    }

    private static string ContentVersion(IReadOnlyList<ResourceRow> rows)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var row in rows.OrderBy(r => r.LangCd, StringComparer.Ordinal).ThenBy(r => r.ResKey, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes($"{row.LangCd}\u001f{row.ResKey}\u001f{row.ResText}\u001e"));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset())[..16];
    }

    private sealed record Snapshot(FrozenDictionary<(string Lang, string Key), string> Texts, IReadOnlyList<LanguageInfo> Languages, string Version);

    internal sealed class LanguageRow
    {
        public string LangCd { get; set; } = "";

        public string LangNm { get; set; } = "";

        public string DefaultYn { get; set; } = "N";
    }

    internal sealed class ResourceRow
    {
        public string ResKey { get; set; } = "";

        public string LangCd { get; set; } = "";

        public string ResText { get; set; } = "";
    }
}

/// <summary>사용 중인 언어.</summary>
/// <param name="LangCd">언어 코드 (ko, en, zh-CN).</param>
/// <param name="LangNm">그 언어로 쓴 이름 (한국어, English, 中文).</param>
/// <param name="IsDefault">기본 언어 여부.</param>
public sealed record LanguageInfo(string LangCd, string LangNm, bool IsDefault);
