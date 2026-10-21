using System.Xml.Linq;

namespace Portal.UnitTests.Architecture;

/// <summary>
/// 모듈 참조 방향 검사(설계서 4장, 14장). 업무 모듈은 프레임워크만 참조하고, 서로 참조하지 않는다.
/// 새 프로젝트를 src에 추가하면 아래 규칙표에도 추가해야 테스트가 통과한다.
/// </summary>
public sealed class ProjectReferenceTests
{
    private static readonly string[] Framework =
        ["Portal.Core", "Portal.Data", "Portal.Security", "Portal.Infrastructure", "Portal.Framework.Web"];

    public static TheoryData<string> SrcProjects()
    {
        var data = new TheoryData<string>();
        foreach (var name in Directory.GetDirectories(RepoPaths.Src).Select(Path.GetFileName).Order(StringComparer.Ordinal))
        {
            data.Add(name!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SrcProjects))]
    public void 프로젝트참조_허용된방향만(string project)
    {
        var allowed = AllowedReferences(project);
        Assert.True(allowed is not null, $"{project}: 참조 규칙이 정의되지 않았습니다. ProjectReferenceTests 규칙표에 추가하세요.");

        var violations = ReferencesOf(project).Where(r => !allowed!.Contains(r)).ToList();

        Assert.True(violations.Count == 0, $"{project}이(가) 허용되지 않은 프로젝트를 참조합니다: {string.Join(", ", violations)}");
    }

    [Fact]
    public void EntityFramework_사용금지()
    {
        var offenders = Directory.GetFiles(RepoPaths.Src, "*.csproj", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(offenders);
    }

    private static HashSet<string>? AllowedReferences(string project) => project switch
    {
        "Portal.Core" => [],
        "Portal.Data" => ["Portal.Core"],
        "Portal.Security" => ["Portal.Core", "Portal.Data"],
        "Portal.Infrastructure" => ["Portal.Core", "Portal.Data"],
        "Portal.Framework.Web" => ["Portal.Core", "Portal.Data", "Portal.Security", "Portal.Infrastructure"],
        _ when project.StartsWith("Portal.Modules.", StringComparison.Ordinal) => [.. Framework],
        "Portal.Web" => [.. AllSrcProjects().Where(p => p is not ("Portal.Web" or "Portal.Worker"))],
        "Portal.Worker" => [.. AllSrcProjects().Where(p => p is not ("Portal.Web" or "Portal.Worker" or "Portal.Framework.Web"))],
        _ => null,
    };

    private static IEnumerable<string> AllSrcProjects() =>
        Directory.GetDirectories(RepoPaths.Src).Select(d => Path.GetFileName(d)!);

    private static IEnumerable<string> ReferencesOf(string project)
    {
        var csproj = Path.Combine(RepoPaths.Src, project, project + ".csproj");
        return XDocument.Load(csproj)
            .Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value.Replace('\\', '/')));
    }
}
