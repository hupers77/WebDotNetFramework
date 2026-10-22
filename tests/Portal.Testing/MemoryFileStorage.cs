using System.Collections.Concurrent;
using Portal.Infrastructure.Files;

namespace Portal.Testing;

/// <summary>메모리에 두는 <see cref="IFileStorage"/> 대역.</summary>
public sealed class MemoryFileStorage : IFileStorage
{
    /// <summary>저장된 파일 (상대 경로 → 내용).</summary>
    public ConcurrentDictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    public async Task WriteAsync(string storedPath, Stream content, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        // 실제 저장소처럼 먼저 파일을 만든 뒤 쓰다가 실패할 수 있다
        Files[storedPath] = [];
        await content.CopyToAsync(buffer, cancellationToken);
        Files[storedPath] = buffer.ToArray();
    }

    public Stream OpenRead(string storedPath) =>
        Files.TryGetValue(storedPath, out var bytes) ? new MemoryStream(bytes, writable: false) : throw new FileNotFoundException(storedPath);

    public void Delete(string storedPath) => Files.TryRemove(storedPath, out _);
}
