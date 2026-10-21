using System.Xml.Linq;

namespace Portal.UnitTests.Architecture;

/// <summary>OSS 허용 목록 검사(설계서 2장). 허용 목록 밖의 패키지는 아키텍트 승인 후 목록과 함께 추가한다.</summary>
public sealed class PackageAllowListTests
{
    [Fact]
    public void 중앙패키지버전_모두허용목록에있음()
    {
        var allowed = File.ReadAllLines(Path.Combine(RepoPaths.Root, "eng", "allowed-packages.txt"))
            .Select(l => l.Split('#')[0].Trim())
            .Where(l => l.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var used = XDocument.Load(Path.Combine(RepoPaths.Root, "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Select(e => e.Attribute("Include")!.Value);

        var notAllowed = used.Where(p => !allowed.Contains(p)).ToList();

        Assert.True(notAllowed.Count == 0, $"허용 목록(eng/allowed-packages.txt)에 없는 패키지: {string.Join(", ", notAllowed)}");
    }

    [Fact]
    public void 프로젝트에서버전직접지정_금지()
    {
        var offenders = Directory.GetFiles(RepoPaths.Root, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => XDocument.Load(f).Descendants("PackageReference").Any(e => e.Attribute("Version") is not null))
            .ToList();

        Assert.Empty(offenders);
    }
}
