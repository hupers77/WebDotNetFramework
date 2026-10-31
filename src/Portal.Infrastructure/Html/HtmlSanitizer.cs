using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Portal.Infrastructure.Html;

/// <summary>
/// 게시글 같은 사용자 HTML을 저장하기 전에 거르는 화이트리스트 필터(설계서 9장 XSS). 허용한 태그와 속성만 남기고 나머지는 지운다.
/// 스크립트·스타일 등 위험한 태그는 안의 내용까지 지우고, 허용하지 않은 일반 태그는 태그만 지우고 글자는 남긴다.
/// 결과는 닫히지 않은 태그를 닫은 균형 잡힌 HTML이다. 링크는 http·https·mailto와 사이트 안 경로만 허용하고 새 창은 <c>rel="noopener noreferrer"</c>를 붙인다.
/// 서식(글자 크기·색·정렬·사진 크기)은 CSP가 style 속성을 막으므로 <c>class</c>의 정해진 이름(<see cref="AllowedClasses"/>)으로만 남긴다.
/// 사진(<c>img</c>)은 첨부 저장소의 사진 주소(<c>/File/Image/{id}</c>)만 허용하고 그 밖의 주소는 태그를 지운다.
/// </summary>
public static partial class HtmlSanitizer
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "p", "br", "hr", "div", "span", "b", "strong", "i", "em", "u", "s", "strike", "sub", "sup", "small",
        "h1", "h2", "h3", "h4", "ul", "ol", "li", "blockquote", "pre", "code",
        "a", "table", "thead", "tbody", "tfoot", "tr", "th", "td", "caption", "img",
    };

    private static readonly HashSet<string> Void = new(StringComparer.Ordinal) { "br", "hr", "img" };

    /// <summary>편집기가 쓰는 서식 클래스 (portal.css의 게시글 서식과 같은 이름). 이 밖의 클래스는 지운다.</summary>
    public static readonly IReadOnlySet<string> AllowedClasses = new HashSet<string>(StringComparer.Ordinal)
    {
        "fs-xs", "fs-sm", "fs-lg", "fs-xl", "fs-xxl",
        "fc-red", "fc-orange", "fc-green", "fc-blue", "fc-purple", "fc-gray",
        "bg-yellow", "bg-green", "bg-blue", "bg-pink", "bg-gray",
        "ta-left", "ta-center", "ta-right", "ta-justify",
        "img-sm", "img-md", "img-lg", "img-full",
    };

    /// <summary>태그와 안의 내용을 통째로 지우는 태그.</summary>
    private static readonly HashSet<string> DropWithContent = new(StringComparer.Ordinal)
    {
        "script", "style", "iframe", "object", "embed", "noscript", "template", "svg", "math", "textarea", "title", "head", "xml", "applet", "frameset", "frame",
    };

    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto" };

    // 태그 안(따옴표 값 포함)에는 '<'가 올 수 없게 한다. 그래야 닫히지 않은 태그를 만나도 다음 '<'에서 바로 멈춰
    // 입력 길이에 비례하는 시간 안에 끝난다('<a<a<a…' 같은 입력으로 서버를 오래 붙잡지 못하게: 서비스 거부 방지).
    // '<'가 든 태그는 태그로 보지 않고 글자로 인코딩하므로 더 안전한 쪽으로만 달라진다.
    [GeneratedRegex("""\G<(/?)([a-zA-Z][a-zA-Z0-9]*)((?:[^<>"']|"[^"<]*"|'[^'<]*')*)>""", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();

    [GeneratedRegex("""([a-zA-Z_:][-a-zA-Z0-9_:.]*)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'>]+)))?""", RegexOptions.CultureInvariant)]
    private static partial Regex AttrPattern();

    /// <summary>사진 주소: 사이트 안의 <c>/File/Image/{id}</c> (가상 디렉터리 아래에 설치해도 된다).</summary>
    [GeneratedRegex(@"^(?:/[A-Za-z0-9._~-]+)*/File/Image/[0-9]{1,19}$", RegexOptions.CultureInvariant)]
    private static partial Regex ImageSrcPattern();

    [GeneratedRegex(@"\G&(?:#[0-9]{1,7}|#[xX][0-9a-fA-F]{1,6}|[a-zA-Z][a-zA-Z0-9]{1,31});", RegexOptions.CultureInvariant)]
    private static partial Regex EntityPattern();

    /// <summary>HTML을 거른다. null이나 빈 값은 빈 문자열.</summary>
    public static string Sanitize(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return "";
        }

        var sb = new StringBuilder(html.Length);
        var open = new Stack<string>();
        var i = 0;
        while (i < html.Length)
        {
            var c = html[i];
            if (c == '<')
            {
                if (string.CompareOrdinal(html, i, "<!--", 0, 4) == 0)
                {
                    var end = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    i = end < 0 ? html.Length : end + 3;
                    continue;
                }

                var m = TagPattern().Match(html, i);
                if (!m.Success)
                {
                    sb.Append("&lt;");
                    i++;
                    continue;
                }

                i += m.Length;
                var closing = m.Groups[1].Length > 0;
                var name = m.Groups[2].Value.ToLowerInvariant();
                if (DropWithContent.Contains(name))
                {
                    if (!closing)
                    {
                        i = SkipTo(html, i, name);
                    }

                    continue;
                }

                if (!Allowed.Contains(name))
                {
                    continue;
                }

                if (closing)
                {
                    CloseTo(sb, open, name);
                }
                else
                {
                    WriteOpen(sb, open, name, m.Groups[3].Value);
                }

                continue;
            }

            if (c == '&')
            {
                var e = EntityPattern().Match(html, i);
                if (e.Success)
                {
                    sb.Append(e.Value);
                    i += e.Length;
                }
                else
                {
                    sb.Append("&amp;");
                    i++;
                }

                continue;
            }

            if (c == '>')
            {
                sb.Append("&gt;");
            }
            else if (c == '"')
            {
                sb.Append("&quot;");
            }
            else if (c == '\'')
            {
                sb.Append("&#39;");
            }
            else if (c == '\0' || (char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
            {
                // 제어 문자는 버린다
            }
            else
            {
                sb.Append(c);
            }

            i++;
        }

        while (open.Count > 0)
        {
            sb.Append("</").Append(open.Pop()).Append('>');
        }

        return sb.ToString();
    }

    /// <summary>태그를 모두 지우고 글자만 남긴다 (검색·미리보기용). 엔티티는 풀어서 준다.</summary>
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return "";
        }

        var text = Regex.Replace(Sanitize(html), @"<(br|/p|/div|/li|/tr|/h[1-4])\b[^>]*>", " ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        text = Regex.Replace(text, "<[^>]*>", "", RegexOptions.CultureInvariant);
        return Regex.Replace(WebUtility.HtmlDecode(text), @"\s+", " ", RegexOptions.CultureInvariant).Trim();
    }

    /// <summary>
    /// <paramref name="name"/>의 닫는 태그(<c>&lt;/name&gt;</c>, 이름 뒤 공백 허용) 바로 다음 위치. 없으면 끝.
    /// 남은 문자열을 복사하지 않고 찾는다(닫는 태그가 많은 입력에서 시간·메모리가 제곱으로 늘지 않게).
    /// </summary>
    private static int SkipTo(string html, int from, string name)
    {
        var marker = "</" + name;
        var at = from;
        while ((at = html.IndexOf(marker, at, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var end = at + marker.Length;
            while (end < html.Length && char.IsWhiteSpace(html[end]))
            {
                end++;
            }

            if (end < html.Length && html[end] == '>')
            {
                return end + 1;
            }

            at += marker.Length;
        }

        return html.Length;
    }

    private static void CloseTo(StringBuilder sb, Stack<string> open, string name)
    {
        if (!open.Contains(name))
        {
            return;
        }

        while (open.Count > 0)
        {
            var top = open.Pop();
            sb.Append("</").Append(top).Append('>');
            if (top == name)
            {
                break;
            }
        }
    }

    private static void WriteOpen(StringBuilder sb, Stack<string> open, string name, string rawAttrs)
    {
        string? href = null, src = null, alt = null, classes = null;
        string? colspan = null, rowspan = null;
        foreach (Match a in AttrPattern().Matches(rawAttrs))
        {
            var key = a.Groups[1].Value.ToLowerInvariant();
            var value = WebUtility.HtmlDecode(a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Value);
            switch (name, key)
            {
                case ("a", "href"):
                    href = SafeUrl(value);
                    break;
                case ("td" or "th", "colspan"):
                    colspan = SafeSpan(value);
                    break;
                case ("td" or "th", "rowspan"):
                    rowspan = SafeSpan(value);
                    break;
                case ("img", "src"):
                    src = ImageSrcPattern().IsMatch(value) ? value : null;
                    break;
                case ("img", "alt"):
                    alt = value.Length > 200 ? value[..200] : value;
                    break;
                case (_, "class"):
                    classes = SafeClasses(value);
                    break;
                default:
                    break;
            }
        }

        if (name == "img" && src is null)
        {
            return;
        }

        sb.Append('<').Append(name);
        if (classes is not null)
        {
            sb.Append(" class=\"").Append(classes).Append('"');
        }

        if (src is not null)
        {
            sb.Append(" src=\"").Append(WebUtility.HtmlEncode(src)).Append("\" alt=\"").Append(WebUtility.HtmlEncode(alt ?? "")).Append('"');
        }

        if (name == "a" && href is not null)
        {
            sb.Append(" href=\"").Append(WebUtility.HtmlEncode(href)).Append("\" rel=\"noopener noreferrer\" target=\"_blank\"");
        }

        if (colspan is not null)
        {
            sb.Append(" colspan=\"").Append(colspan).Append('"');
        }

        if (rowspan is not null)
        {
            sb.Append(" rowspan=\"").Append(rowspan).Append('"');
        }

        if (Void.Contains(name))
        {
            sb.Append(" />");
            return;
        }

        sb.Append('>');
        open.Push(name);
    }

    /// <summary>허용한 서식 클래스만 남긴다. 하나도 없으면 null.</summary>
    private static string? SafeClasses(string value)
    {
        var kept = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Where(AllowedClasses.Contains).Distinct(StringComparer.Ordinal).ToList();
        return kept.Count == 0 ? null : string.Join(' ', kept);
    }

    private static string? SafeSpan(string value) =>
        int.TryParse(value.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 50
            ? n.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;

    /// <summary>허용한 주소만 돌려준다 (http·https·mailto, 사이트 안 경로). 그 밖의 스킴(javascript: 등)은 null.</summary>
    private static string? SafeUrl(string value)
    {
        var url = new string(value.Where(ch => !char.IsControl(ch) && !char.IsWhiteSpace(ch)).ToArray());
        if (url.Length == 0 || url.Length > 2000)
        {
            return null;
        }

        if (url[0] == '#' || (url[0] == '/' && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal)))
        {
            return url;
        }

        var colon = url.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 && AllowedSchemes.Contains(url[..colon]) ? url : null;
    }
}
