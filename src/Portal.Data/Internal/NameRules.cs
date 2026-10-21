using System.Text;
using System.Text.RegularExpressions;

namespace Portal.Data.Internal;

/// <summary>DB 객체·파라미터 이름 규칙(설계서 7장)과 이름 변환.</summary>
internal static partial class NameRules
{
    /// <summary>SP 이름: <c>스키마.USP_대상_동작</c>. 이 형식만 실행하므로 SP 이름 자리로 SQL이 들어올 수 없다.</summary>
    [GeneratedRegex(@"^[A-Z][A-Z0-9]{1,9}\.USP_[A-Z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SpNamePattern();

    /// <summary>TVP 형식 이름: <c>스키마.TVP_대상</c>.</summary>
    [GeneratedRegex(@"^[A-Z][A-Z0-9]{1,9}\.TVP_[A-Z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TvpNamePattern();

    /// <summary>SP 파라미터 이름(<c>@</c> 제외): <c>P_대문자</c>.</summary>
    [GeneratedRegex(@"^P_[A-Z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterNamePattern();

    public static void EnsureSpName(string spName)
    {
        if (!SpNamePattern().IsMatch(spName))
        {
            throw new ArgumentException($"SP 이름 규칙(스키마.USP_대상_동작)에 맞지 않습니다: '{spName}'", nameof(spName));
        }
    }

    public static void EnsureTvpName(string typeName)
    {
        if (!TvpNamePattern().IsMatch(typeName))
        {
            throw new ArgumentException($"TVP 형식 이름 규칙(스키마.TVP_대상)에 맞지 않습니다: '{typeName}'", nameof(typeName));
        }
    }

    public static void EnsureParameterName(string name)
    {
        if (!ParameterNamePattern().IsMatch(name))
        {
            throw new ArgumentException($"SP 파라미터 이름은 'P_대문자' 형식이어야 합니다: '{name}'", nameof(name));
        }
    }

    /// <summary>
    /// 컬럼 이름(<c>CODE_NM</c>)과 속성 이름(<c>CodeNm</c>)을 같은 키로 만든다. 밑줄을 없애고 대문자로 바꾼다.
    /// </summary>
    public static string Normalize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (ch != '_')
            {
                sb.Append(char.ToUpperInvariant(ch));
            }
        }

        return sb.ToString();
    }

    /// <summary>PascalCase 속성 이름을 대문자 스네이크 컬럼 이름으로 바꾼다 (<c>RowVer</c> → <c>ROW_VER</c>).</summary>
    public static string ToUpperSnake(string pascal)
    {
        var sb = new StringBuilder(pascal.Length + 4);
        for (var i = 0; i < pascal.Length; i++)
        {
            var ch = pascal[i];
            if (i > 0 && char.IsUpper(ch) && !char.IsUpper(pascal[i - 1]))
            {
                sb.Append('_');
            }

            sb.Append(char.ToUpperInvariant(ch));
        }

        return sb.ToString();
    }
}
