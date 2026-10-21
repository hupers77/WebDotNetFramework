using System.Text.RegularExpressions;

namespace Portal.UnitTests.Architecture;

/// <summary>
/// 소스 금지 패턴 검사(설계서 9장, 개발 표준 가이드 4·5·10장). 분석기로 잡기 어려운 프로젝트 규칙을 빌드마다 확인한다.
/// </summary>
public sealed partial class SourceRuleTests
{
    // SpExecutor만 SqlCommand를 직접 다룬다.
    private static readonly string SpExecutorPath = Path.Combine("src", "Portal.Data", "SpExecutor.cs");

    public static TheoryData<string, string, string> Rules() => new()
    {
        { "*.cs", @"CommandType\.Text", "인라인 SQL 금지: SP만 사용합니다" },
        { "*.cs", @"\bCommandText\s*=", "SqlCommand 직접 사용 금지: ISpExecutor를 사용합니다" },
        { "*.cs", @"\bDateTime\.(Now|UtcNow|Today)\b", "현재 시각은 TimeProvider로 얻습니다" },
        { "*.cshtml", @"Html\.Raw\s*\(", "Html.Raw 금지: SafeHtml을 사용합니다" },
        { "*.cshtml", @"\son[a-z]+\s*=\s*""", "인라인 이벤트 속성 금지(CSP): addEventListener를 사용합니다" },
        { "*.js", @"\.innerHTML\s*=", "innerHTML 직접 대입 금지: textContent 또는 fw 컴포넌트를 사용합니다" },
        { "*.js", @"\beval\s*\(", "eval 금지" },
    };

    [Theory]
    [MemberData(nameof(Rules))]
    public void 금지패턴_없음(string filePattern, string pattern, string reason)
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant);
        var violations = Directory.GetFiles(RepoPaths.Src, filePattern, SearchOption.AllDirectories)
            .Where(f => !IsGenerated(f))
            .Where(f => !Path.GetRelativePath(RepoPaths.Root, f).Equals(SpExecutorPath, StringComparison.Ordinal))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(x => !IsComment(x.Text) && regex.IsMatch(x.Text))
            .Select(x => $"{Path.GetRelativePath(RepoPaths.Root, x.File)}:{x.Line}")
            .ToList();

        Assert.True(violations.Count == 0, $"{reason}\n{string.Join("\n", violations)}");
    }

    private static bool IsGenerated(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static bool IsComment(string line)
    {
        var t = line.TrimStart();
        return t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith('*') || t.StartsWith("@*", StringComparison.Ordinal);
    }
}
