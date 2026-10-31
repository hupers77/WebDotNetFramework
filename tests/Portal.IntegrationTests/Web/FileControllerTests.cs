using System.Net;
using System.Net.Http.Headers;
using Portal.Core.Keys;
using Portal.Infrastructure.Files;
using static Portal.IntegrationTests.Web.WebTestHttp;

namespace Portal.IntegrationTests.Web;

/// <summary>
/// 첨부 API 권한: 묶음의 프로그램으로 확인한다(목록·다운로드 READ, 업로드·삭제 CREATE 또는 UPDATE).
/// 그다음 묶음의 기록 단위 권한(<c>FW.USP_FILE_GROUP_ACCESS_GET</c>): 볼 수 없는 묶음은 404, 남의 묶음·파일을 바꾸려면 UPDATE.
/// 회사 격리와 기록 단위 판단은 SP가 하므로 <see cref="Fw.FileSpTests"/>가 검증한다.
/// </summary>
public sealed class FileControllerTests : IClassFixture<PortalWebFactory>
{
    private const string Program = "PUR_TEST_MGT";
    private const string Home = "DSH_HOME:READ";
    private const long GroupId = 900;
    private const long FileId = 901;
    private const long ImageId = 902;
    private const long OwnGroupId = 960;
    private const long OwnFileId = 961;
    private const long HiddenGroupId = 950;
    private const long HiddenFileId = 951;
    private const string OwnUser = "file.own.writer";
    private const string StoredPath = "2026/10/0123456789abcdef0123456789abcdef";
    private const string ImagePath = "2026/10/fedcba9876543210fedcba9876543210";
    private static readonly byte[] PdfBytes = [.. "%PDF-1.7\n"u8, .. new byte[50]];
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[30]];
    private readonly PortalWebFactory _web;

    public FileControllerTests(PortalWebFactory web)
    {
        _web = web;
        web.Sp.Returns("FW.USP_FILE_SAVE", new FileUploadResult { FileGroupId = GroupId, FileId = FileId });
        web.Sp.Returns("FW.USP_FILE_GET_LIST", p => (long)p["P_FILE_GROUP_ID"]! == GroupId
            ? new object[]
            {
                new FileGroupDto { FileGroupId = GroupId, ProgramId = Program, CompanyCd = "HQ" },
                new List<FileItemDto> { new() { FileId = FileId, OrgFileNm = "견적서.pdf", FileExt = "pdf", FileSize = PdfBytes.Length } },
            }
            : new object[] { new List<FileGroupDto>(), new List<FileItemDto>() });
        web.Sp.Returns("FW.USP_FILE_GET", p => (long)p["P_FILE_ID"]! switch
        {
            FileId => new StoredFileDto { FileId = FileId, FileGroupId = GroupId, ProgramId = Program, OrgFileNm = "견적서 최종.pdf", FileExt = "pdf", StoredPath = StoredPath },
            ImageId => new StoredFileDto { FileId = ImageId, FileGroupId = GroupId, ProgramId = Program, OrgFileNm = "현장.png", FileExt = "png", StoredPath = ImagePath },
            OwnFileId => new StoredFileDto { FileId = OwnFileId, FileGroupId = GroupId, ProgramId = Program, OrgFileNm = "내 파일.pdf", FileExt = "pdf", StoredPath = StoredPath, RegId = OwnUser },
            HiddenFileId => new StoredFileDto { FileId = HiddenFileId, FileGroupId = HiddenGroupId, ProgramId = Program, OrgFileNm = "비밀글 첨부.png", FileExt = "png", StoredPath = ImagePath },
            _ => null,
        });
        // 기록 단위 권한: GroupId는 남이 만든 공개 묶음, OwnGroupId는 내 묶음, HiddenGroupId는 볼 수 없는 글(비밀글 등)의 묶음
        web.Sp.Returns("FW.USP_FILE_GROUP_ACCESS_GET", p => (long)p["P_FILE_GROUP_ID"]! switch
        {
            GroupId => new FileGroupAccessDto { FileGroupId = GroupId, OwnerYn = "N", ViewYn = "Y" },
            OwnGroupId => new FileGroupAccessDto { FileGroupId = OwnGroupId, OwnerYn = "Y", ViewYn = "Y" },
            HiddenGroupId => new FileGroupAccessDto { FileGroupId = HiddenGroupId, OwnerYn = "N", ViewYn = (string?)p["P_MANAGER_YN"] == "Y" ? "Y" : "N" },
            _ => null,
        });
        web.Files.Files[StoredPath] = PdfBytes;
        web.Files.Files[ImagePath] = PngBytes;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private int CallCount(string spName) => _web.Sp.Calls.Count(c => c.SpName == spName);

    private async Task<HttpResponseMessage> PostAsync(string token, HttpRequestMessage request, bool csrf = true)
    {
        var (csrfToken, cookie) = await GetCsrfAsync(_web.NewClient(token), "/");
        if (csrf)
        {
            request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        }

        return await _web.NewClient(token, cookie).SendAsync(request, Ct);
    }

    private static HttpRequestMessage UploadRequest(string query, string fileName = "견적서.pdf")
    {
        var request = Ajax(HttpMethod.Post, "/File/Upload?" + query);
        var content = new ByteArrayContent(PdfBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        request.Content = new MultipartFormDataContent { { content, "file", fileName } };
        return request;
    }

    [Theory]
    [InlineData("CREATE", OwnGroupId)]
    [InlineData("UPDATE", GroupId)]
    public async Task 업로드_등록권한은내묶음에_수정권한은남의묶음에도저장된다(string func, long groupId)
    {
        var token = _web.SignIn("file.writer." + func, [Home, $"{Program}:{func}"]);

        var response = await PostAsync(token, UploadRequest($"programId={Program}&fileGroupId={groupId}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await JsonAsync(response);
        Assert.Equal(GroupId, json.GetProperty("data").GetProperty("fileGroupId").GetInt64());
        var call = _web.Sp.LastCall("FW.USP_FILE_SAVE");
        Assert.Equal((Program, groupId), ((string)call.Parameters["P_PROGRAM_ID"]!, (long)call.Parameters["P_FILE_GROUP_ID"]!));
        Assert.Equal(PdfBytes, _web.Files.Files[(string)call.Parameters["P_STORED_PATH"]!]);
    }

    [Fact]
    public async Task 업로드_등록권한만으로_남의묶음에는더할수없다()
    {
        var token = _web.SignIn("file.writer.others", [Home, $"{Program}:CREATE"]);
        var before = CallCount("FW.USP_FILE_SAVE");
        var stored = _web.Files.Files.Count;

        var response = await PostAsync(token, UploadRequest($"programId={Program}&fileGroupId={GroupId}"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, CallCount("FW.USP_FILE_SAVE"));
        Assert.Equal(stored, _web.Files.Files.Count);
    }

    [Fact]
    public async Task 볼수없는묶음은_목록_다운로드_사진_ZIP모두_없는것처럼()
    {
        var reader = _web.SignIn("file.reader.hidden", [Home, $"{Program}:READ"]);
        var manager = _web.SignIn("file.manager.hidden", [Home, $"{Program}:READ", $"{Program}:UPDATE"]);
        _web.Sp.Returns("FW.USP_FILE_GET_LIST", p => new object[]
        {
            new FileGroupDto { FileGroupId = (long)p["P_FILE_GROUP_ID"]!, ProgramId = Program, CompanyCd = "HQ" },
            new List<FileItemDto> { new() { FileId = (long)p["P_FILE_GROUP_ID"]! == HiddenGroupId ? HiddenFileId : FileId, OrgFileNm = "a.pdf", FileExt = "pdf", FileSize = PdfBytes.Length } },
        });
        var before = CallCount("FW.USP_FILE_DOWNLOAD_EXEC");

        var list = await _web.NewClient(reader).SendAsync(Ajax(HttpMethod.Get, $"/File/List?fileGroupId={HiddenGroupId}"), Ct);
        var download = await _web.NewClient(reader).GetAsync($"/File/Download/{HiddenFileId}", Ct);
        var image = await _web.NewClient(reader).GetAsync($"/File/Image/{HiddenFileId}", Ct);
        var zip = await _web.NewClient(reader).GetAsync($"/File/DownloadZip/{HiddenGroupId}", Ct);
        var asManager = await _web.NewClient(manager).GetAsync($"/File/Image/{HiddenFileId}", Ct);

        foreach (var response in new[] { list, download, image, zip })
        {
            Assert.Contains(MessageKeys.NotFound, await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }

        Assert.Equal(before, CallCount("FW.USP_FILE_DOWNLOAD_EXEC"));
        Assert.Equal("N", (string?)_web.Sp.Calls.First(c => c.SpName == "FW.USP_FILE_GROUP_ACCESS_GET" && (long)c.Parameters["P_FILE_GROUP_ID"]! == HiddenGroupId).Parameters["P_MANAGER_YN"]);
        Assert.Equal(HttpStatusCode.OK, asManager.StatusCode);
        Assert.Equal("image/png", asManager.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task 삭제_등록권한은자기가올린파일만_남의파일은수정권한()
    {
        var own = _web.SignIn(OwnUser, [Home, $"{Program}:CREATE"]);
        var other = _web.SignIn("file.writer.notmine", [Home, $"{Program}:CREATE"]);
        var before = CallCount("FW.USP_FILE_DEL");

        var denied = await PostAsync(other, Ajax(HttpMethod.Post, "/File/Delete", new { fileId = OwnFileId }));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(before, CallCount("FW.USP_FILE_DEL"));

        var ok = await PostAsync(own, Ajax(HttpMethod.Post, "/File/Delete", new { fileId = OwnFileId }));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(before + 1, CallCount("FW.USP_FILE_DEL"));
    }

    [Fact]
    public async Task 업로드_조회권한만있으면403_저장하지않는다()
    {
        var token = _web.SignIn("file.reader.up", [Home, $"{Program}:READ"]);
        var before = CallCount("FW.USP_FILE_SAVE");
        var stored = _web.Files.Files.Count;

        var response = await PostAsync(token, UploadRequest($"programId={Program}"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, CallCount("FW.USP_FILE_SAVE"));
        Assert.Equal(stored, _web.Files.Files.Count);
    }

    [Fact]
    public async Task 업로드_CSRF토큰없으면거부()
    {
        var token = _web.SignIn("file.nocsrf", [Home, $"{Program}:CREATE"]);

        var response = await PostAsync(token, UploadRequest($"programId={Program}"), csrf: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task 비밀번호변경강제중에는_첨부API를쓸수없다()
    {
        var token = _web.SignIn("file.pwdchg", [Home, $"{Program}:READ"], passwordChangeRequired: true);

        var response = await _web.NewClient(token).SendAsync(Ajax(HttpMethod.Get, $"/File/List?fileGroupId={GroupId}"), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 로그인전_401()
    {
        var response = await _web.NewClient().SendAsync(Ajax(HttpMethod.Get, $"/File/List?fileGroupId={GroupId}"), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task 목록_묶음프로그램의조회권한으로판단()
    {
        var reader = _web.SignIn("file.reader.list", [Home, $"{Program}:READ"]);
        var other = _web.SignIn("file.other.list", [Home, "SYS_CODE_MGT:READ"]);

        var ok = await _web.NewClient(reader).SendAsync(Ajax(HttpMethod.Get, $"/File/List?fileGroupId={GroupId}"), Ct);
        var denied = await _web.NewClient(other).SendAsync(Ajax(HttpMethod.Get, $"/File/List?fileGroupId={GroupId}"), Ct);

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var files = (await JsonAsync(ok)).GetProperty("data").GetProperty("files");
        Assert.Equal("견적서.pdf", files[0].GetProperty("orgFileNm").GetString());
        Assert.False(files[0].TryGetProperty("storedPath", out _));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task 다운로드_첨부로내려주고횟수를올린다()
    {
        var token = _web.SignIn("file.reader.down", [Home, $"{Program}:READ"]);
        var before = CallCount("FW.USP_FILE_DOWNLOAD_EXEC");

        var response = await _web.NewClient(token).GetAsync($"/File/Download/{FileId}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("견적서 최종.pdf", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal(PdfBytes, await response.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal(before + 1, CallCount("FW.USP_FILE_DOWNLOAD_EXEC"));
    }

    [Fact]
    public async Task 다운로드_조회권한없으면403_횟수도올리지않는다()
    {
        var token = _web.SignIn("file.other.down", [Home, "SYS_CODE_MGT:READ"]);
        var before = CallCount("FW.USP_FILE_DOWNLOAD_EXEC");

        var response = await _web.NewClient(token).GetAsync($"/File/Download/{FileId}", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, CallCount("FW.USP_FILE_DOWNLOAD_EXEC"));
    }

    [Fact]
    public async Task 본문사진_조회권한이면_화면에보이고_다운로드횟수는_올리지않는다()
    {
        var token = _web.SignIn("file.reader.image", [Home, $"{Program}:READ"]);
        var before = CallCount("FW.USP_FILE_DOWNLOAD_EXEC");

        var response = await _web.NewClient(token).GetAsync($"/File/Image/{ImageId}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.Equal(PngBytes, await response.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal(before, CallCount("FW.USP_FILE_DOWNLOAD_EXEC"));
    }

    [Fact]
    public async Task 본문사진_사진이아닌파일은_없는것처럼_조회권한없으면403()
    {
        var reader = _web.SignIn("file.reader.image2", [Home, $"{Program}:READ"]);
        var other = _web.SignIn("file.other.image", [Home, "SYS_CODE_MGT:READ"]);

        var pdf = await _web.NewClient(reader).GetAsync($"/File/Image/{FileId}", Ct);
        var denied = await _web.NewClient(other).GetAsync($"/File/Image/{ImageId}", Ct);

        // 업무 오류는 공통 응답 형식(JSON, MSG.NOT_FOUND)으로 온다
        Assert.Equal("application/json", pdf.Content.Headers.ContentType?.MediaType);
        var body = await pdf.Content.ReadAsStringAsync(Ct);
        Assert.Contains(MessageKeys.NotFound, body, StringComparison.Ordinal);
        Assert.DoesNotContain("%PDF", body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task 삭제_조회권한만있으면403_수정권한이면삭제()
    {
        var reader = _web.SignIn("file.reader.del", [Home, $"{Program}:READ"]);
        var writer = _web.SignIn("file.writer.del", [Home, $"{Program}:READ", $"{Program}:UPDATE"]);
        var before = CallCount("FW.USP_FILE_DEL");

        var denied = await PostAsync(reader, Ajax(HttpMethod.Post, "/File/Delete", new { fileId = FileId }));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(before, CallCount("FW.USP_FILE_DEL"));

        var ok = await PostAsync(writer, Ajax(HttpMethod.Post, "/File/Delete", new { fileId = FileId }));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(before + 1, CallCount("FW.USP_FILE_DEL"));
    }

    [Fact]
    public async Task ZIP다운로드_조회권한이면ZIP_없으면403()
    {
        var reader = _web.SignIn("file.reader.zip", [Home, $"{Program}:READ"]);
        var other = _web.SignIn("file.other.zip", [Home, "SYS_CODE_MGT:READ"]);

        var ok = await _web.NewClient(reader).GetAsync($"/File/DownloadZip/{GroupId}", Ct);
        var denied = await _web.NewClient(other).GetAsync($"/File/DownloadZip/{GroupId}", Ct);

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("application/zip", ok.Content.Headers.ContentType?.MediaType);
        using var archive = new System.IO.Compression.ZipArchive(await ok.Content.ReadAsStreamAsync(Ct));
        Assert.Equal(["견적서 최종.pdf"], archive.Entries.Select(e => e.FullName));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task 화면레이아웃이_첨부한도설정을내려준다()
    {
        var token = _web.SignIn("file.layout", [Home]);

        var html = await (await _web.NewClient(token).GetAsync("/", Ct)).Content.ReadAsStringAsync(Ct);

        Assert.Contains("<meta name=\"fw-file-max-mb\" content=\"20\" />", html, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"fw-file-max-count\" content=\"10\" />", html, StringComparison.Ordinal);
    }
}
