using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Portal.Core.Config;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Data;

namespace Portal.Infrastructure.Files;

/// <summary>
/// 첨부 업로드·다운로드·삭제 (설계서 9장, 개발 표준 가이드 체크리스트 "FileService만 썼다").
/// 확장자 차단, 내용 확인(매직 넘버), 크기 제한, GUID 저장 이름, SHA-256 기록을 여기서 하고,
/// 허용 확장자·개수·회사 격리는 SP가 다시 확인한다. 프로그램 권한은 호출하는 Controller가 확인한다.
/// </summary>
public sealed class FileService(ISpExecutor sp, IFileStorage storage, IPortalConfig config, TimeProvider time, ILogger<FileService> logger)
{
    private const long Megabyte = 1024 * 1024;

    /// <summary>파일 1개 최대 크기(MB, <c>FILE.MAX_SIZE_MB</c>).</summary>
    public int MaxSizeMb => config.GetInt(ConfigKeys.FileMaxSizeMb);

    /// <summary>파일 1개 최대 크기(바이트).</summary>
    public long MaxSizeBytes => MaxSizeMb * Megabyte;

    /// <summary>
    /// 파일 하나를 올린다. 묶음 ID가 없으면 새 임시 묶음을 만든다(업무 저장 SP가 <c>USP_FILE_GROUP_CONFIRM_EXEC</c>로 확정).
    /// </summary>
    /// <param name="programId">권한 기준 프로그램.</param>
    /// <param name="fileGroupId">추가할 묶음. 새 묶음이면 null.</param>
    /// <param name="fileName">사용자가 올린 파일 이름 (경로가 붙어 있으면 이름만 쓴다).</param>
    /// <param name="content">내용. 읽기만 한다.</param>
    /// <param name="cancellationToken">취소 토큰.</param>
    public async Task<FileUploadResult> UploadAsync(string programId, long? fileGroupId, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        var name = CleanName(fileName);
        var ext = ExtensionOf(name);
        if (ext.Length == 0 || FileSignatures.IsBlocked(ext))
        {
            throw new BizException(FileMessageKeys.ExtDenied);
        }

        var maxBytes = MaxSizeBytes;
        var now = time.GetUtcNow();
        var storedPath = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}";
        var (size, hash) = await WriteCheckedAsync(storedPath, ext, content, maxBytes, cancellationToken);

        try
        {
            return await sp.QuerySingleAsync<FileUploadResult>("FW.USP_FILE_SAVE", new
            {
                P_FILE_GROUP_ID = fileGroupId,
                P_PROGRAM_ID = programId,
                P_ORG_FILE_NM = name,
                P_FILE_EXT = ext,
                P_FILE_SIZE = size,
                P_STORED_PATH = storedPath,
                P_CONTENT_HASH = hash,
            }, cancellationToken) ?? throw new InvalidOperationException("USP_FILE_SAVE가 결과를 돌려주지 않았습니다.");
        }
        catch
        {
            DeleteQuietly(storedPath);
            throw;
        }
    }

    /// <summary>묶음의 파일 목록. 없거나 볼 수 없는 묶음(다른 회사)이면 null.</summary>
    public Task<FileListResult?> GetListAsync(long fileGroupId, CancellationToken cancellationToken = default) =>
        sp.QueryMultipleAsync("FW.USP_FILE_GET_LIST", new { P_FILE_GROUP_ID = fileGroupId }, async r =>
        {
            var group = await r.ReadSingleAsync<FileGroupDto>(cancellationToken);
            var files = await r.ReadAsync<FileItemDto>(cancellationToken);
            return group is null ? null : new FileListResult(group, files);
        }, cancellationToken);

    /// <summary>파일 한 건. 없거나 받을 수 없는 파일(다른 회사)이면 null.</summary>
    public Task<StoredFileDto?> GetAsync(long fileId, CancellationToken cancellationToken = default) =>
        sp.QuerySingleAsync<StoredFileDto>("FW.USP_FILE_GET", new { P_FILE_ID = fileId }, cancellationToken);

    /// <summary>다운로드 횟수를 올리고 내용을 연다. 호출자가 READ 권한을 확인한 뒤 부른다.</summary>
    public async Task<Stream> OpenDownloadAsync(StoredFileDto file, CancellationToken cancellationToken = default)
    {
        var stream = storage.OpenRead(file.StoredPath);
        await sp.ExecuteAsync("FW.USP_FILE_DOWNLOAD_EXEC", new { P_FILE_ID = file.FileId }, cancellationToken);
        return stream;
    }

    /// <summary>삭제 표시. 저장소의 내용은 정리 작업이 지운다(업무 저장 전 취소해도 되돌릴 수 있게).</summary>
    public Task DeleteAsync(long fileId, CancellationToken cancellationToken = default) =>
        sp.ExecuteAsync("FW.USP_FILE_DEL", new { P_FILE_ID = fileId }, cancellationToken);

    /// <summary>내용을 확인하며 저장소에 쓴다. 크기 초과·내용 불일치면 쓴 것을 지우고 업무 오류를 던진다.</summary>
    private async Task<(long Size, string Hash)> WriteCheckedAsync(string storedPath, string ext, Stream content, long maxBytes, CancellationToken cancellationToken)
    {
        var head = new byte[FileSignatures.HeadLength];
        var headLength = await content.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken);
        if (headLength == 0)
        {
            throw new BizException(FileMessageKeys.Empty);
        }

        if (!FileSignatures.Matches(ext, head.AsSpan(0, headLength)))
        {
            throw new BizException(FileMessageKeys.ContentMismatch);
        }

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var checking = new CheckingStream(content, head.AsMemory(0, headLength), hasher, maxBytes);
        try
        {
            await storage.WriteAsync(storedPath, checking, cancellationToken);
        }
        catch
        {
            DeleteQuietly(storedPath);
            if (checking.Exceeded)
            {
                throw new BizException(FileMessageKeys.TooLarge, maxBytes / Megabyte);
            }

            throw;
        }

        return (checking.Length, Convert.ToHexStringLower(hasher.GetHashAndReset()));
    }

    private void DeleteQuietly(string storedPath)
    {
        try
        {
            storage.Delete(storedPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 남은 파일은 DB에 없으므로 정리 작업이 지운다
            logger.LogWarning(ex, "업로드 실패 후 저장 파일을 지우지 못했습니다: {StoredPath}", storedPath);
        }
    }

    /// <summary>경로를 떼고 제어 문자를 없앤 파일 이름 (200자까지).</summary>
    internal static string CleanName(string fileName)
    {
        var name = fileName.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        name = new string([.. name.Where(c => !char.IsControl(c))]).Trim().TrimEnd('.');
        if (name.Length == 0)
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        if (name.Length > 200)
        {
            var ext = ExtensionOf(name);
            name = ext.Length is > 0 and < 20 ? name[..(199 - ext.Length)] + "." + ext : name[..200];
        }

        return name;
    }

    /// <summary>소문자 확장자 (점 없이). 없으면 빈 문자열.</summary>
    internal static string ExtensionOf(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot <= 0 || dot == name.Length - 1 ? "" : name[(dot + 1)..].ToLowerInvariant();
    }

    /// <summary>이미 읽은 앞부분을 먼저 내보내고, 읽는 동안 해시와 크기를 잰다. 한도를 넘으면 읽기를 멈춘다.</summary>
    private sealed class CheckingStream(Stream inner, ReadOnlyMemory<byte> head, IncrementalHash hasher, long maxBytes) : Stream
    {
        private ReadOnlyMemory<byte> pending = head;

        public bool Exceeded { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => Position;

        public override long Position { get; set; }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read;
            if (!pending.IsEmpty)
            {
                read = Math.Min(buffer.Length, pending.Length);
                pending[..read].CopyTo(buffer);
                pending = pending[read..];
            }
            else
            {
                read = await inner.ReadAsync(buffer, cancellationToken);
            }

            Position += read;
            if (Position > maxBytes)
            {
                Exceeded = true;
                throw new InvalidDataException("파일 크기 한도를 넘었습니다.");
            }

            hasher.AppendData(buffer.Span[..read]);
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
