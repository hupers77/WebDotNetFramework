using Portal.Core.Errors;
using Portal.Infrastructure.Files;
using Portal.IntegrationTests.Infrastructure;

namespace Portal.IntegrationTests.Fw;

/// <summary>첨부 SP: 묶음 생성·추가, 확장자·크기·개수 제한, 협력사 회사 격리와 공유 묶음, 다운로드 횟수, 삭제, 확정.</summary>
/// <remarks>SP 오류는 테스트 트랜잭션도 롤백하므로 오류 확인은 각 테스트의 마지막 호출로 둔다.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class FileSpTests(DbFixture fixture)
{
    private const string Program = "SYS_CODE_MGT";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<FileUploadResult?> SaveAsync(TestDb db, long? groupId = null, string ext = "pdf", long size = 1000, string program = Program) =>
        db.Sp.QuerySingleAsync<FileUploadResult>("FW.USP_FILE_SAVE", new
        {
            P_FILE_GROUP_ID = groupId,
            P_PROGRAM_ID = program,
            P_ORG_FILE_NM = "견적서." + ext,
            P_FILE_EXT = ext,
            P_FILE_SIZE = size,
            P_STORED_PATH = $"2026/10/{Guid.NewGuid():N}",
            P_CONTENT_HASH = new string('a', 64),
        }, Ct);

    private static Task<FileListResult?> ListAsync(TestDb db, long groupId) =>
        db.Sp.QueryMultipleAsync("FW.USP_FILE_GET_LIST", new { P_FILE_GROUP_ID = groupId }, async r =>
        {
            var group = await r.ReadSingleAsync<FileGroupDto>(Ct);
            var files = await r.ReadAsync<FileItemDto>(Ct);
            return group is null ? null : new FileListResult(group, files);
        }, Ct);

    private static Task<StoredFileDto?> GetAsync(TestDb db, long fileId) =>
        db.Sp.QuerySingleAsync<StoredFileDto>("FW.USP_FILE_GET", new { P_FILE_ID = fileId }, Ct);

    /// <summary>협력사 두 곳과 각 회사 사용자를 만든다.</summary>
    private static async Task SeedPartnersAsync(TestDb db)
    {
        await db.SeedCompanyAsync("P9001");
        await db.SeedCompanyAsync("P9002");
        await db.SeedUserAsync("p1.user", "P9001", "PARTNER", roles: ["PARTNER_USER"]);
        await db.SeedUserAsync("p2.user", "P9002", "PARTNER", roles: ["PARTNER_USER"]);
    }

    [Fact]
    public async Task 새묶음_올린사용자회사의임시묶음_추가하면같은묶음()
    {
        await using var db = await fixture.BeginAsync();

        var first = await SaveAsync(db);
        var second = await SaveAsync(db, first!.FileGroupId, "xlsx");

        Assert.Equal(first.FileGroupId, second!.FileGroupId);
        var list = await ListAsync(db, first.FileGroupId);
        Assert.NotNull(list);
        Assert.Equal((Program, "HQ", "N", "N"), (list.Group.ProgramId, list.Group.CompanyCd, list.Group.ShareYn, list.Group.ConfirmYn));
        Assert.Equal([first.FileId, second.FileId], list.Files.Select(f => f.FileId));
        Assert.Equal(["pdf", "xlsx"], list.Files.Select(f => f.FileExt));
    }

    [Fact]
    public async Task 공통코드에없는확장자_거부()
    {
        await using var db = await fixture.BeginAsync();

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, ext: "exe"));

        Assert.Equal(FileMessageKeys.ExtDenied, ex.MessageKey);
    }

    [Fact]
    public async Task 사용안함으로바꾼확장자_거부()
    {
        await using var db = await fixture.BeginAsync();
        await db.ExecAsync("UPDATE FW.TB_CODE SET USE_YN = 'N' WHERE GROUP_CD = 'FILE_EXT' AND CODE = 'zip'");

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, ext: "zip"));

        Assert.Equal(FileMessageKeys.ExtDenied, ex.MessageKey);
    }

    [Fact]
    public async Task 설정크기초과_거부()
    {
        await using var db = await fixture.BeginAsync();
        var maxMb = await db.ScalarAsync<long>("SELECT CAST(CONFIG_VAL AS BIGINT) FROM FW.TB_CONFIG WHERE CONFIG_KEY = 'FILE.MAX_SIZE_MB'");

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, size: (maxMb * 1048576) + 1));

        Assert.Equal(FileMessageKeys.TooLarge, ex.MessageKey);
    }

    [Fact]
    public async Task 설정개수초과_거부()
    {
        await using var db = await fixture.BeginAsync();
        await db.ExecAsync("UPDATE FW.TB_CONFIG SET CONFIG_VAL = N'2' WHERE CONFIG_KEY = 'FILE.MAX_COUNT'");
        var first = await SaveAsync(db);
        await SaveAsync(db, first!.FileGroupId);

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, first.FileGroupId));

        Assert.Equal(FileMessageKeys.CountExceeded, ex.MessageKey);
    }

    [Fact]
    public async Task 다른프로그램묶음에추가_없는것으로응답()
    {
        await using var db = await fixture.BeginAsync();
        var first = await SaveAsync(db);

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, first!.FileGroupId, program: "SYS_CONFIG_MGT"));

        Assert.Equal("MSG.NOT_FOUND", ex.MessageKey);
    }

    [Fact]
    public async Task 협력사_다른회사묶음은목록과파일이보이지않는다_본사는보인다()
    {
        await using var db = await fixture.BeginAsync();
        await SeedPartnersAsync(db);
        db.AsUser("p1.user", "P9001", "PARTNER");
        var saved = await SaveAsync(db);
        Assert.Equal("P9001", (await ListAsync(db, saved!.FileGroupId))!.Group.CompanyCd);

        db.AsUser("p2.user", "P9002", "PARTNER");
        Assert.Null(await ListAsync(db, saved.FileGroupId));
        Assert.Null(await GetAsync(db, saved.FileId));

        db.AsUser("it.admin", "HQ", "EMP");
        Assert.NotNull(await ListAsync(db, saved.FileGroupId));
        Assert.NotNull(await GetAsync(db, saved.FileId));
    }

    [Fact]
    public async Task 협력사_다른회사묶음에추가할수없다()
    {
        await using var db = await fixture.BeginAsync();
        await SeedPartnersAsync(db);
        db.AsUser("p1.user", "P9001", "PARTNER");
        var saved = await SaveAsync(db);

        db.AsUser("p2.user", "P9002", "PARTNER");
        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, saved!.FileGroupId));

        Assert.Equal("MSG.NOT_FOUND", ex.MessageKey);
    }

    [Fact]
    public async Task 공유묶음_다른회사협력사도받지만_지울수는없다()
    {
        await using var db = await fixture.BeginAsync();
        await SeedPartnersAsync(db);
        var saved = await SaveAsync(db);
        await db.Sp.ExecuteAsync("FW.USP_FILE_GROUP_CONFIRM_EXEC", new { P_FILE_GROUP_ID = saved!.FileGroupId, P_PROGRAM_ID = Program, P_SHARE_YN = "Y" }, Ct);

        db.AsUser("p2.user", "P9002", "PARTNER");
        var list = await ListAsync(db, saved.FileGroupId);
        Assert.Equal(("Y", "Y"), (list!.Group.ShareYn, list.Group.ConfirmYn));
        Assert.NotNull(await GetAsync(db, saved.FileId));

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync("FW.USP_FILE_DEL", new { P_FILE_ID = saved.FileId }, Ct));
        Assert.Equal("MSG.NOT_FOUND", ex.MessageKey);
    }

    [Fact]
    public async Task 다운로드횟수증가_삭제하면목록과단건에서빠진다()
    {
        await using var db = await fixture.BeginAsync();
        var saved = await SaveAsync(db);

        await db.Sp.ExecuteAsync("FW.USP_FILE_DOWNLOAD_EXEC", new { P_FILE_ID = saved!.FileId }, Ct);
        await db.Sp.ExecuteAsync("FW.USP_FILE_DOWNLOAD_EXEC", new { P_FILE_ID = saved.FileId }, Ct);
        Assert.Equal(2, (await ListAsync(db, saved.FileGroupId))!.Files.Single().DownCnt);
        Assert.Equal(saved.FileGroupId, (await GetAsync(db, saved.FileId))!.FileGroupId);

        await db.Sp.ExecuteAsync("FW.USP_FILE_DEL", new { P_FILE_ID = saved.FileId }, Ct);

        Assert.Empty((await ListAsync(db, saved.FileGroupId))!.Files);
        Assert.Null(await GetAsync(db, saved.FileId));
        Assert.Equal("Y", await db.ScalarAsync<string>("SELECT DEL_YN FROM FW.TB_FILE WHERE FILE_ID = @F", ("@F", saved.FileId)));
    }

    [Fact]
    public async Task 확정_다른프로그램이면거부()
    {
        await using var db = await fixture.BeginAsync();
        var saved = await SaveAsync(db);

        var ex = await Assert.ThrowsAsync<BizException>(() =>
            db.Sp.ExecuteAsync("FW.USP_FILE_GROUP_CONFIRM_EXEC", new { P_FILE_GROUP_ID = saved!.FileGroupId, P_PROGRAM_ID = "SYS_CONFIG_MGT" }, Ct));

        Assert.Equal("MSG.NOT_FOUND", ex.MessageKey);
    }
}
