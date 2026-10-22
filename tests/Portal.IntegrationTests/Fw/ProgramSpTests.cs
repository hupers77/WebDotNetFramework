using System.Text.Json;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.IntegrationTests.Infrastructure;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.IntegrationTests.Fw;

/// <summary>
/// 프로그램 관리 SP: 목록(기능·연결 현황), 기능 선택지(서버 경로·역할 수), 등록(형식·중복·조회 필수), 수정(ID·모듈·URL 고정, 감사 로그·캐시),
/// 기능 빼기(서버 경로가 요구하면 거부, 역할 권한 함께 삭제), 홈·프로그램 관리 끄기 금지, 삭제(연결된 프로그램 거부), 동시성.
/// </summary>
/// <remarks>SP 오류는 테스트 트랜잭션도 롤백하므로 오류 확인은 각 테스트의 마지막 호출로 둔다.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class ProgramSpTests(DbFixture fixture)
{
    private const string Prog = "IT_PG_MGT";
    private const string Admin = "it.pg.admin";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>메뉴·서버 경로가 없는 테스트 프로그램(조회·등록·출력, 역할 권한 1개)과 포털 관리자를 만든다.</summary>
    private static async Task<TestDb> BeginAsync(DbFixture fixture)
    {
        var db = await fixture.BeginAsync();
        await db.ExecAsync("""
            INSERT INTO FW.TB_PROGRAM (PROGRAM_ID, MODULE_CD, PROGRAM_NM, URL) VALUES ('IT_PG_MGT', 'IT', N'테스트 프로그램', '/It/Pg');
            INSERT INTO FW.TB_PROGRAM_FUNC (PROGRAM_ID, FUNC_CD) VALUES ('IT_PG_MGT', 'READ'), ('IT_PG_MGT', 'CREATE'), ('IT_PG_MGT', 'PRINT');
            INSERT INTO FW.TB_ROLE_PROGRAM_FUNC (ROLE_ID, PROGRAM_ID, FUNC_CD) VALUES ('EMP_USER', 'IT_PG_MGT', 'READ'), ('EMP_USER', 'IT_PG_MGT', 'PRINT');
            """);
        await db.SeedUserAsync(Admin, "HQ", "EMP", null, "PORTAL_ADMIN");
        db.AsUser(Admin, "HQ", "EMP");
        return db;
    }

    private static async Task<ProgramDto> ProgramAsync(TestDb db, string programId) =>
        Assert.Single(await db.Sp.QueryAsync<ProgramDto>(ProgramAdminService.ListSp, null, Ct), p => p.ProgramId == programId);

    private static async Task<int> UpdateAsync(TestDb db, string programId, string[] funcs, string? useYn = null, string? name = null, byte[]? rowVer = null)
    {
        var p = await ProgramAsync(db, programId);
        return (await db.Sp.ExecuteAsync(ProgramAdminService.SaveSp, new
        {
            P_PROGRAM_ID = programId,
            P_ROW_VER = rowVer ?? p.RowVer,
            P_MODULE_CD = "ZZ",
            P_PROGRAM_NM = name ?? p.ProgramNm,
            P_URL = "/Changed",
            P_PII_YN = "Y",
            P_USE_YN = useYn ?? p.UseYn,
            P_FUNCS = JsonSerializer.Serialize(funcs),
        }, Ct)).AffectedCnt;
    }

    private static Task<Portal.Data.SaveResult> CreateAsync(TestDb db, string programId, string url, params string[] funcs) =>
        db.Sp.ExecuteAsync(ProgramAdminService.SaveSp, new
        {
            P_PROGRAM_ID = programId,
            P_MODULE_CD = "IT",
            P_PROGRAM_NM = "새 프로그램",
            P_URL = url,
            P_PII_YN = "N",
            P_USE_YN = "Y",
            P_FUNCS = JsonSerializer.Serialize(funcs),
        }, Ct);

    private static Task<List<string>> FuncsAsync(TestDb db, string programId) =>
        db.ColumnAsync<string>("SELECT FUNC_CD FROM FW.TB_PROGRAM_FUNC WHERE PROGRAM_ID = @P ORDER BY FUNC_CD", ("@P", programId));

    private static Task<long> VersionAsync(TestDb db, string key) => db.ScalarAsync<long>("SELECT VERSION_NO FROM FW.TB_CACHE_VERSION WHERE CACHE_KEY = @K", ("@K", key));

    [Fact]
    public async Task 목록_기능은코드순서_연결현황()
    {
        await using var db = await BeginAsync(fixture);

        var test = await ProgramAsync(db, Prog);
        var user = await ProgramAsync(db, "SYS_USER_MGT");

        Assert.Equal(("READ,CREATE,PRINT", 0, 0, 1), (test.Funcs, test.MenuCnt, test.ApiCnt, test.RoleCnt));
        Assert.True(user.MenuCnt > 0 && user.ApiCnt > 0);
    }

    [Fact]
    public async Task 기능선택지_사용중인코드전부_서버경로와역할수()
    {
        await using var db = await BeginAsync(fixture);

        var test = await db.Sp.QueryAsync<ProgramFuncDto>(ProgramAdminService.FuncListSp, new { P_PROGRAM_ID = Prog }, Ct);
        var user = await db.Sp.QueryAsync<ProgramFuncDto>(ProgramAdminService.FuncListSp, new { P_PROGRAM_ID = "SYS_USER_MGT" }, Ct);

        Assert.Equal("READ", test[0].FuncCd);
        Assert.Equal(("Y", 1), (test.Single(f => f.FuncCd == "PRINT").UsedYn, test.Single(f => f.FuncCd == "PRINT").RoleCnt));
        Assert.Equal("N", test.Single(f => f.FuncCd == "EXPORT").UsedYn);
        Assert.True(user.Single(f => f.FuncCd == "EXPORT").ApiCnt > 0);
    }

    [Fact]
    public async Task 수정_ID모듈URL은그대로_기능빼면역할권한도삭제_감사로그_캐시()
    {
        await using var db = await BeginAsync(fixture);
        var (auth, menu) = (await VersionAsync(db, "AUTH"), await VersionAsync(db, "MENU"));

        Assert.Equal(4, await UpdateAsync(db, Prog, ["READ", "EXPORT"], name: "바뀐 이름")); // 프로그램 1 + 뺀 기능 2(CREATE·PRINT) + 넣은 기능 1(EXPORT)
        var p = await ProgramAsync(db, Prog);

        Assert.Equal(("IT", "/It/Pg", "바뀐 이름", "Y"), (p.ModuleCd, p.Url, p.ProgramNm, p.PiiYn));
        Assert.Equal(["EXPORT", "READ"], await FuncsAsync(db, Prog));
        Assert.Equal(["READ"], await db.ColumnAsync<string>("SELECT FUNC_CD FROM FW.TB_ROLE_PROGRAM_FUNC WHERE PROGRAM_ID = 'IT_PG_MGT'"));
        Assert.Equal((auth + 1, menu + 1), (await VersionAsync(db, "AUTH"), await VersionAsync(db, "MENU")));
        var audit = await db.ScalarAsync<string>("SELECT TOP (1) CONCAT(BEFORE_JSON, '|', AFTER_JSON) FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_PROGRAM_FUNC' AND ROW_KEY = @P ORDER BY LOG_ID DESC", ("@P", Prog));
        Assert.Equal("""["CREATE","PRINT","READ"]|["EXPORT","READ"]""", audit);
    }

    [Fact]
    public async Task 서버경로가요구하는기능은_뺄수없다()
    {
        await using var db = await BeginAsync(fixture);
        var funcs = (await ProgramAsync(db, "SYS_USER_MGT")).Funcs.Split(',').Where(f => f != "EXPORT").ToArray();

        var ex = await Assert.ThrowsAsync<BizException>(() => UpdateAsync(db, "SYS_USER_MGT", funcs));

        Assert.Equal(ProgramMessageKeys.FuncInUse, ex.MessageKey);
    }

    [Fact]
    public async Task 조회없이는_거부()
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => UpdateAsync(db, Prog, ["CREATE"]));

        Assert.Equal(ProgramMessageKeys.ReadRequired, ex.MessageKey);
    }

    [Fact]
    public async Task 없는기능코드는_거부()
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => UpdateAsync(db, Prog, ["READ", "NOPE"]));

        Assert.Equal(MessageKeys.InvalidInput, ex.MessageKey);
    }

    [Theory]
    [InlineData("DSH_HOME")]
    [InlineData("SYS_PROGRAM_MGT")]
    public async Task 홈과프로그램관리는_끌수없다(string programId)
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(async () => await UpdateAsync(db, programId, (await ProgramAsync(db, programId)).Funcs.Split(','), useYn: "N"));

        Assert.Equal(ProgramMessageKeys.KeepUse, ex.MessageKey);
    }

    [Fact]
    public async Task 등록_감사로그_같은ID는중복()
    {
        await using var db = await BeginAsync(fixture);

        await CreateAsync(db, "IT_PG_NEW", "/It/New", "READ", "PRINT");

        var p = await ProgramAsync(db, "IT_PG_NEW");
        Assert.Equal(("IT", "/It/New", "READ,PRINT", Admin), (p.ModuleCd, p.Url, p.Funcs, p.UpdId));
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_PROGRAM' AND ACTION_CD = 'I' AND ROW_KEY = 'IT_PG_NEW'"));
        var ex = await Assert.ThrowsAsync<BizException>(() => CreateAsync(db, "IT_PG_NEW", "/It/New", "READ"));
        Assert.Equal(MessageKeys.DupCode, ex.MessageKey);
    }

    [Theory]
    [InlineData("it_pg_new", "/It/New")]
    [InlineData("IT_PG_NEW", "It/New")]
    [InlineData("IT_PG_NEW", "//evil.example")]
    [InlineData("IT_PG_NEW", "/It/<x>")]
    public async Task 등록_ID나주소형식이틀리면거부(string programId, string url)
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => CreateAsync(db, programId, url, "READ"));

        Assert.Equal(MessageKeys.InvalidInput, ex.MessageKey);
    }

    [Fact]
    public async Task 삭제_기능과권한도함께_감사로그()
    {
        await using var db = await BeginAsync(fixture);
        var p = await ProgramAsync(db, Prog);

        Assert.Equal(4, (await db.Sp.ExecuteAsync(ProgramAdminService.DeleteSp, new { P_PROGRAM_ID = Prog, P_ROW_VER = p.RowVer }, Ct)).AffectedCnt);

        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_PROGRAM WHERE PROGRAM_ID = 'IT_PG_MGT'"));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_ROLE_PROGRAM_FUNC WHERE PROGRAM_ID = 'IT_PG_MGT'"));
        Assert.Equal("""["EMP_USER:PRINT","EMP_USER:READ"]""", await db.ScalarAsync<string>("SELECT TOP (1) BEFORE_JSON FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_ROLE_PROGRAM_FUNC' AND ACTION_CD = 'D' AND ROW_KEY = 'IT_PG_MGT' ORDER BY LOG_ID DESC"));
        Assert.Equal("""["CREATE","PRINT","READ"]""", await db.ScalarAsync<string>("SELECT TOP (1) JSON_QUERY(BEFORE_JSON, '$.FUNCS') FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_PROGRAM' AND ACTION_CD = 'D' AND ROW_KEY = 'IT_PG_MGT' ORDER BY LOG_ID DESC"));
    }

    [Fact]
    public async Task 삭제_코드나메뉴가쓰는프로그램은거부()
    {
        await using var db = await BeginAsync(fixture);
        var p = await ProgramAsync(db, "SYS_USER_MGT");

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync(ProgramAdminService.DeleteSp, new { P_PROGRAM_ID = p.ProgramId, P_ROW_VER = p.RowVer }, Ct));

        Assert.Equal(ProgramMessageKeys.InUse, ex.MessageKey);
    }

    [Fact]
    public async Task 다른사람이먼저고쳤으면_동시성오류()
    {
        await using var db = await BeginAsync(fixture);
        var stale = (await ProgramAsync(db, Prog)).RowVer;
        await UpdateAsync(db, Prog, ["READ"], name: "먼저");

        var ex = await Assert.ThrowsAsync<BizException>(() => UpdateAsync(db, Prog, ["READ"], name: "나중", rowVer: stale));

        Assert.Equal(MessageKeys.Concurrency, ex.MessageKey);
    }
}
