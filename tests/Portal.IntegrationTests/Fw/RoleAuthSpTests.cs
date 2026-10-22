using System.Text.Json;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.IntegrationTests.Infrastructure;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.IntegrationTests.Fw;

/// <summary>
/// 역할별 권한 SP: 역할 목록(내 역할 표시), 권한표, 통째 저장(추가·삭제·변경 없음), 감사 로그·권한 캐시·ROW_VER,
/// 조회 필수, SYS_ADMIN 역할·내 역할 보호, 회사 유형에 열리지 않은 화면, 사용 안 함 프로그램 보존, 동시성.
/// </summary>
/// <remarks>SP 오류는 테스트 트랜잭션도 롤백하므로 오류 확인은 각 테스트의 마지막 호출로 둔다.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class RoleAuthSpTests(DbFixture fixture)
{
    private const string HqRole = "IT_RA_HQ";
    private const string PtnRole = "IT_RA_PTN";
    private const string Admin = "it.ra.admin";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>본사용·협력사용 테스트 역할과 포털 관리자(현재 사용자)를 만든다.</summary>
    private static async Task<TestDb> BeginAsync(DbFixture fixture)
    {
        var db = await fixture.BeginAsync();
        await db.ExecAsync("""
            INSERT INTO FW.TB_ROLE (ROLE_ID, ROLE_NM, COMPANY_TYPE_CD, SORT_NO) VALUES ('IT_RA_HQ', N'본사 테스트', 'HQ', 900), ('IT_RA_PTN', N'협력사 테스트', 'PARTNER', 901);
            INSERT INTO FW.TB_ROLE_PROGRAM_FUNC (ROLE_ID, PROGRAM_ID, FUNC_CD) VALUES ('IT_RA_HQ', 'SYS_CODE_MGT', 'READ'), ('IT_RA_HQ', 'SYS_CODE_MGT', 'UPDATE');
            """);
        await db.SeedUserAsync(Admin, "HQ", "EMP", null, "PORTAL_ADMIN");
        db.AsUser(Admin, "HQ", "EMP");
        return db;
    }

    private static async Task<RoleDto> RoleAsync(TestDb db, string roleId) =>
        Assert.Single(await db.Sp.QueryAsync<RoleDto>(RoleAuthService.RoleListSp, null, Ct), r => r.RoleId == roleId);

    private static Task<IReadOnlyList<RoleAuthRow>> ListAsync(TestDb db, string roleId) =>
        db.Sp.QueryAsync<RoleAuthRow>(RoleAuthService.ListSp, new { P_ROLE_ID = roleId }, Ct);

    private static async Task<int> SaveAsync(TestDb db, string roleId, params string[] funcs) =>
        (await db.Sp.ExecuteAsync(RoleAuthService.SaveSp, new
        {
            P_ROLE_ID = roleId,
            P_ROW_VER = (await RoleAsync(db, roleId)).RowVer,
            P_FUNCS = JsonSerializer.Serialize(funcs.Select(f => f.Split(':')).Select(p => new { programId = p[0], funcCd = p[1] })),
        }, Ct)).AffectedCnt;

    private static Task<List<string>> GrantsAsync(TestDb db, string roleId) =>
        db.ColumnAsync<string>("SELECT PROGRAM_ID + ':' + FUNC_CD FROM FW.TB_ROLE_PROGRAM_FUNC WHERE ROLE_ID = @R ORDER BY PROGRAM_ID, FUNC_CD", ("@R", roleId));

    private static Task<long> AuthVersionAsync(TestDb db) => db.ScalarAsync<long>("SELECT VERSION_NO FROM FW.TB_CACHE_VERSION WHERE CACHE_KEY = 'AUTH'");

    [Fact]
    public async Task 역할목록_기능수와내역할표시()
    {
        await using var db = await BeginAsync(fixture);

        var hq = await RoleAsync(db, HqRole);
        var mine = await RoleAsync(db, "PORTAL_ADMIN");

        Assert.Equal((2, "N", "HQ"), (hq.FuncCnt, hq.MyRoleYn, hq.CompanyTypeCd));
        Assert.Equal("Y", mine.MyRoleYn);
    }

    [Fact]
    public async Task 권한표_프로그램이쓰는기능만_부여여부와회사유형()
    {
        await using var db = await BeginAsync(fixture);

        var hq = await ListAsync(db, HqRole);
        var ptn = await ListAsync(db, PtnRole);

        Assert.Equal(["READ", "UPDATE"], hq.Where(r => r.ProgramId == "SYS_CODE_MGT" && r.GrantedYn == "Y").Select(r => r.FuncCd));
        Assert.Contains(hq, r => r.ProgramId == "SYS_CODE_MGT" && r.FuncCd == "UPLOAD" && r.GrantedYn == "N");
        Assert.DoesNotContain(hq, r => r.ProgramId == "SYS_CODE_MGT" && r.FuncCd == "APPROVE");
        Assert.Equal(("EMP", "Y", "N"), (hq.First(r => r.ProgramId == "SYS_USER_MGT").OpenScopeCd, hq.First(r => r.ProgramId == "SYS_USER_MGT").GrantableYn, ptn.First(r => r.ProgramId == "SYS_USER_MGT").GrantableYn));
        Assert.Equal(("N", "Y"), (hq.First(r => r.ProgramId == "PTN_USER_MGT").GrantableYn, ptn.First(r => r.ProgramId == "PTN_USER_MGT").GrantableYn));
    }

    [Fact]
    public async Task 저장_추가와삭제_감사로그_캐시_ROW_VER_같은상태면0()
    {
        await using var db = await BeginAsync(fixture);
        var before = await AuthVersionAsync(db);
        var oldVer = (await RoleAsync(db, HqRole)).RowVer;

        var changed = await SaveAsync(db, HqRole, "SYS_CODE_MGT:READ", "SYS_CODE_MGT:EXPORT", "SYS_USER_MGT:READ");
        var again = await SaveAsync(db, HqRole, "SYS_CODE_MGT:READ", "SYS_CODE_MGT:EXPORT", "SYS_USER_MGT:READ");

        Assert.Equal((3, 0), (changed, again)); // UPDATE 삭제, EXPORT·SYS_USER_MGT:READ 추가
        Assert.Equal(["SYS_CODE_MGT:EXPORT", "SYS_CODE_MGT:READ", "SYS_USER_MGT:READ"], await GrantsAsync(db, HqRole));
        Assert.Equal(before + 1, await AuthVersionAsync(db));
        Assert.NotEqual(oldVer, (await RoleAsync(db, HqRole)).RowVer);
        var audit = await db.ScalarAsync<string>("SELECT TOP (1) CONCAT(BEFORE_JSON, '|', AFTER_JSON) FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_ROLE_PROGRAM_FUNC' AND ROW_KEY = @R ORDER BY LOG_ID DESC", ("@R", HqRole));
        Assert.Equal("""["SYS_CODE_MGT:READ","SYS_CODE_MGT:UPDATE"]|["SYS_CODE_MGT:EXPORT","SYS_CODE_MGT:READ","SYS_USER_MGT:READ"]""", audit);
    }

    [Fact]
    public async Task 저장_모두빼면빈목록_사용안함프로그램권한은그대로()
    {
        await using var db = await BeginAsync(fixture);
        await db.ExecAsync("INSERT INTO FW.TB_ROLE_PROGRAM_FUNC (ROLE_ID, PROGRAM_ID, FUNC_CD) VALUES ('IT_RA_HQ', 'SYS_JOB_MGT', 'READ'); UPDATE FW.TB_PROGRAM SET USE_YN = 'N' WHERE PROGRAM_ID = 'SYS_JOB_MGT'");

        Assert.Equal(2, await SaveAsync(db, HqRole));

        Assert.Equal(["SYS_JOB_MGT:READ"], await GrantsAsync(db, HqRole));
    }

    [Fact]
    public async Task 협력사역할_열리지않은화면은새로못주고_있던권한은뺄수있다()
    {
        await using var db = await BeginAsync(fixture);
        await db.ExecAsync("INSERT INTO FW.TB_ROLE_PROGRAM_FUNC (ROLE_ID, PROGRAM_ID, FUNC_CD) VALUES ('IT_RA_PTN', 'SYS_CODE_MGT', 'READ')");
        Assert.Equal(2, await SaveAsync(db, PtnRole, "PTN_USER_MGT:READ"));

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, PtnRole, "PTN_USER_MGT:READ", "SYS_USER_MGT:READ"));

        Assert.Equal(RoleAuthMessageKeys.Scope, ex.MessageKey);
    }

    [Fact]
    public async Task 조회없이다른기능만은_거부()
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, HqRole, "SYS_CODE_MGT:UPDATE"));

        Assert.Equal(RoleAuthMessageKeys.ReadRequired, ex.MessageKey);
    }

    [Fact]
    public async Task 프로그램이쓰지않는기능은_거부()
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, HqRole, "SYS_CODE_MGT:READ", "SYS_CODE_MGT:APPROVE"));

        Assert.Equal(MessageKeys.InvalidInput, ex.MessageKey);
    }

    [Fact]
    public async Task 시스템관리자역할은_못고친다()
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, "SYS_ADMIN"));

        Assert.Equal(RoleAuthMessageKeys.SysAdmin, ex.MessageKey);
    }

    [Fact]
    public async Task 내가가진역할은_못고치고_시스템관리자는고친다()
    {
        await using var db = await BeginAsync(fixture);
        await db.SeedUserAsync("it.ra.root", "HQ", "EMP", null, "SYS_ADMIN", "PORTAL_ADMIN");
        db.AsUser("it.ra.root", "HQ", "EMP");
        Assert.Equal(1, await SaveAsync(db, "PORTAL_ADMIN", [.. (await GrantsAsync(db, "PORTAL_ADMIN")).Where(g => g != "SYS_CODE_MGT:UPLOAD")]));
        db.AsUser(Admin, "HQ", "EMP");

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, "PORTAL_ADMIN"));

        Assert.Equal(RoleAuthMessageKeys.Self, ex.MessageKey);
    }

    [Fact]
    public async Task 다른사람이먼저저장하면_동시성오류()
    {
        await using var db = await BeginAsync(fixture);
        var stale = (await RoleAsync(db, HqRole)).RowVer;
        await SaveAsync(db, HqRole, "SYS_CODE_MGT:READ");

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync(RoleAuthService.SaveSp, new { P_ROLE_ID = HqRole, P_ROW_VER = stale, P_FUNCS = "[]" }, Ct));

        Assert.Equal(MessageKeys.Concurrency, ex.MessageKey);
    }
}
