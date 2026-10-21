namespace Portal.UnitTests.Architecture;

/// <summary>레포지토리 루트(<c>Portal.sln</c>가 있는 폴더)를 찾는다.</summary>
internal static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string Src => Path.Combine(Root, "src");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Portal.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Portal.sln을 찾을 수 없습니다.");
    }
}
