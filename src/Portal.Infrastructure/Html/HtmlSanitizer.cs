using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Portal.Infrastructure.Html;

/// <summary>
/// 게시글 같은 사용자 HTML을 저장하기 전에 거르는 화이트리스트 필터(설계서 9장 XSS). 허용한 태그와 속성만 남기고 나머지는 지운다.
/// 스크립트·스타일 등 위험한 태그는 안의 내용까지 지우고, 허용하지 않은 일반 태그는 태그만 지우고 글자는 남긴다.
/// 결과는 닫히지 않은 태그를 닫은 균형 잡힌 HTML이다. 링크는 http·https·mailto와 사이트 안 경로만 허용하고 새 창은 <c>rel="noopener noreferrer"</c>를 붙인다.
/// </summary>
public static partial class HtmlSanitizer
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "p", "br", "hr", "div", "span", "b", "strong", "i", "em", "u", "s", "strike", "sub", "sup", "small",
        "h1", "h2", "h3", "h4", "ul", "ol", "li", "blockquote", "pre", "code",
        "a", "table", "thead", "tbody", "tfoot", "tr", "th", "td", "caption",
    };

    private static readonly HashSet<string> Void = new(StringComparer.Ordinal) { "br", "hr" };

    /// <summary>태그와 안의 내용을 통째로 지우는 태그.</summary>
    private static readonly HashSet<string> DropWithContent = new(StringComparer.Ordinal)
    {
        "script", "style", "iframe", "object", "embed", "noscript", "template", "svg", "math", "textarea", "title", "head", "xml", "applet", "frameset", "frame",
    };

    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto" };

    [GeneratedRegex("""\G<(/?)([a-zA-Z][a-zA-Z0-9]*)((?:[^>"']|"[^"]*"|'[^']*')*)>""", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();

    [GeneratedRegex("""([a-zA-Z_:][-a-zA-Z0-9_:.]*)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'>]+)))?""", RegexOptions.CultureInvariant)]
    private static partial Regex AttrPattern();

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

    private static int SkipTo(string html, int from, string name)
    {
        var m = Regex.Match(html[from..], $@"</{name}\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return m.Success ? from + m.Index + m.Length : html.Length;
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
        sb.Append('<').Append(name);
        string? href = null;
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
                default:
                    break;
            }
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
