using System.Net;
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

    [GeneratedRegex(@"\{\{([A-Z0-9_]{1,50})\}\}")]
    private static partial Regex Variable();
}
