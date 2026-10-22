using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Portal.Infrastructure.Messaging;

/// <summary>
/// 메시지 템플릿 치환 (초기 데이터 정의서 7장). <c>{{USER_NM}}</c>처럼 대문자·숫자·밑줄로 된 변수를 값으로 바꾼다.
/// 본문(HTML)에 넣는 값은 HTML 인코딩하고, 제목에 넣는 값은 줄바꿈·제어 문자를 지운다(메일 헤더 주입 방지). 없는 변수는 빈 값이 된다.
/// </summary>
public static partial class MessageRenderer
{
    /// <summary>HTML 본문 치환. 값은 HTML 인코딩한다(템플릿의 HTML 태그는 그대로).</summary>
    public static string RenderBody(string template, IReadOnlyDictionary<string, string?> values) =>
        Variable().Replace(template, m => WebUtility.HtmlEncode(values.GetValueOrDefault(m.Groups[1].Value) ?? ""));

    /// <summary>제목 치환. 결과에서 줄바꿈·제어 문자를 지우고 앞뒤 공백을 없앤다.</summary>
    public static string RenderSubject(string template, IReadOnlyDictionary<string, string?> values)
    {
        var text = Variable().Replace(template, m => values.GetValueOrDefault(m.Groups[1].Value) ?? "");
        return new string([.. text.Where(c => !char.IsControl(c))]).Trim();
    }

    /// <summary>
    /// HTML 본문을 글자만 남긴 문장으로 바꾼다(포털 알림 내용). 문단·줄바꿈은 줄로 바꾸고 태그를 지운 뒤 HTML 엔터티를 푼다.
    /// 결과는 화면에서 글자(textContent)로만 넣는다.
    /// </summary>
    public static string ToPlainText(string html)
    {
        var text = LineBreak().Replace(html, "\n");
        text = WebUtility.HtmlDecode(Tag().Replace(text, ""));
        var lines = text.Split('\n').Select(l => Spaces().Replace(l, " ").Trim()).Where(l => l.Length > 0);
        return string.Join('\n', lines);
    }

    /// <summary>업무 SP가 넘긴 치환값 JSON(<c>{"USER_NM":"..."}</c>)을 읽는다. 문자열이 아닌 값은 JSON 글자 그대로, null은 null.</summary>
    public static Dictionary<string, string?> ParseValues(string? json)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        using var doc = json is { Length: > 0 } ? JsonDocument.Parse(json) : null;
        if (doc?.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                values[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Null or JsonValueKind.Undefined => null,
                    _ => property.Value.GetRawText(),
                };
            }
        }

        return values;
    }

    [GeneratedRegex(@"\{\{([A-Z0-9_]{1,50})\}\}")]
    private static partial Regex Variable();

    [GeneratedRegex(@"<br\s*/?>|</(p|div|li|h[1-6]|tr)>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"[ \t\r\f\v]+")]
    private static partial Regex Spaces();
}
