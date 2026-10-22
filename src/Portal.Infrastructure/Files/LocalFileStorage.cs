using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Portal.Infrastructure.Files;

/// <summary>서버 로컬(또는 공유) 폴더 저장소. 웹서버가 한 대이므로 로컬 디스크를 기본으로 한다.</summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string root;

    /// <summary>저장소를 만든다. 루트 폴더가 없으면 만든다.</summary>
    public LocalFileStorage(IOptions<FileStorageOptions> options, IHostEnvironment environment)
    {
        var configured = options.Value.StorageRoot;
        root = Path.GetFullPath(configured is { Length: > 0 } ? configured : Path.Combine(environment.ContentRootPath, "App_Data", "files"));
        Directory.CreateDirectory(root);
    }

    /// <inheritdoc />
    public async Task WriteAsync(string storedPath, Stream content, CancellationToken cancellationToken = default)
    {
        var path = Resolve(storedPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(target, cancellationToken);
    }

    /// <inheritdoc />
    public Stream OpenRead(string storedPath) =>
        new FileStream(Resolve(storedPath), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

    /// <inheritdoc />
    public void Delete(string storedPath) => File.Delete(Resolve(storedPath));

    /// <summary>상대 경로를 절대 경로로 바꾼다. 루트 밖이면 거부한다(경로 조작 방지, 설계서 9장).</summary>
    internal string Resolve(string storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath) || Path.IsPathRooted(storedPath))
        {
            throw new ArgumentException("저장 경로가 올바르지 않습니다.", nameof(storedPath));
        }

        var full = Path.GetFullPath(Path.Combine(root, storedPath));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("저장소 밖 경로입니다.", nameof(storedPath));
        }

        return full;
    }
}
