using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portal.Core.Errors;
using Portal.Infrastructure.Files;
using Portal.Testing;

namespace Portal.UnitTests.Infrastructure;

public sealed class FileServiceTests
{
    private static readonly byte[] PdfBytes = [.. "%PDF-1.7\n"u8, .. new byte[100]];

    private readonly FakeSpExecutor _sp = new();
    private readonly MemoryFileStorage _storage = new();
    private readonly FakePortalConfig _config = new();
    private readonly FileService _service;

    public FileServiceTests()
    {
        _sp.Returns("FW.USP_FILE_SAVE", new FileUploadResult { FileGroupId = 7, FileId = 70 });
        _service = new FileService(_sp, _storage, _config, new FakeTimeProvider(new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero)), NullLogger<FileService>.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<FileUploadResult> UploadAsync(string name, byte[] content, long? groupId = null) =>
        _service.UploadAsync("SYS_CODE_MGT", groupId, name, new MemoryStream(content), Ct);

    [Fact]
    public async Task 정상업로드_GUID경로에저장하고_SP에이름확장자크기해시를넘긴다()
    {
        var result = await UploadAsync(@"C:\Users\kim\견적서.PDF", PdfBytes, 7);

        Assert.Equal((7, 70), (result.FileGroupId, result.FileId));
        var call = _sp.LastCall("FW.USP_FILE_SAVE");
        Assert.Equal("견적서.PDF", call.Parameters["P_ORG_FILE_NM"]);
        Assert.Equal("pdf", call.Parameters["P_FILE_EXT"]);
        Assert.Equal((long)PdfBytes.Length, call.Parameters["P_FILE_SIZE"]);
        Assert.Equal(7L, call.Parameters["P_FILE_GROUP_ID"]);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(PdfBytes)), call.Parameters["P_CONTENT_HASH"]);
        var path = Assert.IsType<string>(call.Parameters["P_STORED_PATH"]);
        Assert.Matches("^2026/10/[0-9a-f]{32}$", path);
        Assert.Equal(PdfBytes, _storage.Files[path]);
    }

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("page.HTML")]
    [InlineData("logo.svg")]
    [InlineData("run.js")]
    [InlineData("noext")]
    [InlineData(".pdf")]
    public async Task 실행스크립트형식과확장자없는파일_거부_저장하지않는다(string name)
    {
        var ex = await Assert.ThrowsAsync<BizException>(() => UploadAsync(name, PdfBytes));

        Assert.Equal(FileMessageKeys.ExtDenied, ex.MessageKey);
        Assert.Empty(_storage.Files);
        Assert.Empty(_sp.Calls);
    }

    [Theory]
    [InlineData("a.pdf", "MZ\u0090\u0000")]
    [InlineData("a.xlsx", "%PDF-1.7")]
    [InlineData("a.png", "GIF89a")]
    [InlineData("a.txt", "abc\u0000def")]
    [InlineData("a.csv", "#!/bin/sh")]
    [InlineData("a.dat", "\u007fELF")]
    public async Task 내용이확장자와다르면_거부(string name, string content)
    {
        var ex = await Assert.ThrowsAsync<BizException>(() => UploadAsync(name, Encoding.Latin1.GetBytes(content)));

        Assert.Equal(FileMessageKeys.ContentMismatch, ex.MessageKey);
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task 빈파일_거부()
    {
        var ex = await Assert.ThrowsAsync<BizException>(() => UploadAsync("a.pdf", []));

        Assert.Equal(FileMessageKeys.Empty, ex.MessageKey);
    }

    [Fact]
    public async Task 설정크기초과_쓰던파일을지우고_한도MB를알린다()
    {
        _config.Set("FILE.MAX_SIZE_MB", "1");
        var big = new byte[(1024 * 1024) + 1];
        PdfBytes.CopyTo(big, 0);

        var ex = await Assert.ThrowsAsync<BizException>(() => UploadAsync("big.pdf", big));

        Assert.Equal(FileMessageKeys.TooLarge, ex.MessageKey);
        Assert.Equal([1L], ex.Args);
        Assert.Empty(_storage.Files);
        Assert.Empty(_sp.Calls);
    }

    [Fact]
    public async Task SP가거부하면_저장한파일을지운다()
    {
        _sp.Throws("FW.USP_FILE_SAVE", new BizException(FileMessageKeys.CountExceeded));

        var ex = await Assert.ThrowsAsync<BizException>(() => UploadAsync("a.pdf", PdfBytes));

        Assert.Equal(FileMessageKeys.CountExceeded, ex.MessageKey);
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task 다운로드_횟수를올리고내용을연다()
    {
        _storage.Files["2026/10/abc"] = PdfBytes;

        await using var stream = await _service.OpenDownloadAsync(new StoredFileDto { FileId = 70, StoredPath = "2026/10/abc" }, Ct);

        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, Ct);
        Assert.Equal(PdfBytes, copy.ToArray());
        Assert.Equal(70L, _sp.LastCall("FW.USP_FILE_DOWNLOAD_EXEC").Parameters["P_FILE_ID"]);
    }

    [Theory]
    [InlineData("../../web.config", "web.config")]
    [InlineData("a/b\\c.pdf", "c.pdf")]
    [InlineData(" 보고서\u0000\u0007.pdf. ", "보고서.pdf")]
    public void 파일이름_경로와제어문자를뗀다(string input, string expected) =>
        Assert.Equal(expected, FileService.CleanName(input));

    [Fact]
    public void 긴파일이름_확장자를살려200자로자른다()
    {
        var name = FileService.CleanName(new string('가', 300) + ".xlsx");

        Assert.Equal(200, name.Length);
        Assert.EndsWith(".xlsx", name, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("2026/../../outside")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    public void 로컬저장소_루트밖경로는거부(string storedPath)
    {
        var storage = NewLocalStorage(out _);

        Assert.Throws<ArgumentException>(() => storage.Resolve(storedPath));
    }

    [Fact]
    public async Task 로컬저장소_쓰고읽고지운다_같은경로에두번쓸수없다()
    {
        var storage = NewLocalStorage(out var root);
        try
        {
            await storage.WriteAsync("2026/10/abc", new MemoryStream(PdfBytes), Ct);
            await using (var read = storage.OpenRead("2026/10/abc"))
            {
                Assert.Equal(PdfBytes.Length, read.Length);
            }

            await Assert.ThrowsAsync<IOException>(() => storage.WriteAsync("2026/10/abc", new MemoryStream(PdfBytes), Ct));
            storage.Delete("2026/10/abc");
            Assert.False(File.Exists(Path.Combine(root, "2026", "10", "abc")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static LocalFileStorage NewLocalStorage(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "portal-files-" + Guid.NewGuid().ToString("N"));
        return new LocalFileStorage(Options.Create(new FileStorageOptions { StorageRoot = root }), new TestHostEnvironment());
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";

        public string ApplicationName { get; set; } = "Portal";

        public string ContentRootPath { get; set; } = Path.GetTempPath();

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
