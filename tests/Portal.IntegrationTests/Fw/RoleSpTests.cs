using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.IntegrationTests.Infrastructure;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.IntegrationTests.Fw;

/// <summary>
/// 역할 관리 SP: 목록(부여 현황), 등록(역할 ID 형식·중복), 수정(감사 로그·권한 캐시), 협력사 역할 범위, 시스템 역할·SYS_ADMIN·내 역할 보호,
/// 쓰이는 역할의 회사 유형 고정, 삭제(기능 권한 함께·부여된 역할 거부), 동시성.
/// </summary>
/// <remarks>SP 오류는 테스트 트랜잭션도 롤백하므로 오류 확인은 각 테스트의 마지막 호출로 둔다.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class RoleSpTests(DbFixture fixture)
{
    private const string Role = "IT_RM_ROLE";
    private const string Admin = "it.rm.admin";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>테스트 역할(기능 권한 1개)과 포털 관리자(현재 사용자)를 만든다.</summary>
    private static async Task<TestDb> BeginAsync(DbFixture fixture)
    {
        var db = await fixture.BeginAsync();
        await db.ExecAsync("""
            INSERT INTO FW.TB_ROLE (ROLE_ID, ROLE_NM, COMPANY_TYPE_CD, SORT_NO) VALUES ('IT_RM_ROLE', N'관리 테스트', 'HQ', 900);
            INSERT INTO FW.TB_ROLE_PROGRAM_FUNC (ROLE_ID, PROGRAM_ID, FUNC_CD) VALUES ('IT_RM_ROLE', 'SYS_CODE_MGT', 'READ');
            """);
        await db.SeedUserAsync(Admin, "HQ", "EMP", null, "PORTAL_ADMIN");
        db.AsUser(Admin, "HQ", "EMP");
        return db;
    }

    private static async Task<RoleDto> RoleAsync(TestDb db, string roleId) =>
        Assert.Single(await db.Sp.QueryAsync<RoleDto>(RoleAuthService.RoleListSp, null, Ct), r => r.RoleId == roleId);

    private static object Params(RoleDto role, string? companyTypeCd = "keep", string? dataScopeCd = null, string? useYn = null, string? roleNm = null) => new
    {
        P_ROLE_ID = role.RoleId,
        P_ROW_VER = role.RowVer,
        P_ROLE_NM = roleNm ?? role.RoleNm,
        P_DESCRIPTION = role.Description,
        P_COMPANY_TYPE_CD = companyTypeCd == "keep" ? role.CompanyTypeCd : companyTypeCd,
        P_DATA_SCOPE_CD = dataScopeCd ?? role.DataScopeCd,
        P_SORT_NO = role.SortNo,
        P_USE_YN = useYn ?? role.UseYn,
    };

    private static async Task UpdateAsync(TestDb db, string roleId, string? companyTypeCd = "keep", string? dataScopeCd = null, string? useYn = null, string? roleNm = null) =>
        await db.Sp.ExecuteAsync(RoleAdminService.SaveSp, Params(await RoleAsync(db, roleId), companyTypeCd, dataScopeCd, useYn, roleNm), Ct);

    private static Task<Portal.Data.SaveResult> CreateAsync(TestDb db, string roleId, string? companyTypeCd = null, string dataScopeCd = "SELF") =>
        db.Sp.ExecuteAsync(RoleAdminService.SaveSp, new
        {
            P_ROLE_ID = roleId,
            P_ROLE_NM = "새 역할",
            P_COMPANY_TYPE_CD = companyTypeCd,
            P_DATA_SCOPE_CD = dataScopeCd,
            P_SORT_NO = 910,
            P_USE_YN = "Y",
        }, Ct);

    private static async Task<int> DeleteAsync(TestDb db, string roleId) =>
        (await db.Sp.ExecuteAsync(RoleAdminService.DeleteSp, new { P_ROLE_ID = roleId, P_ROW_VER = (await RoleAsync(db, roleId)).RowVer }, Ct)).AffectedCnt;

    private static Task<long> AuthVersionAsync(TestDb db) => db.ScalarAsync<long>("SELECT VERSION_NO FROM FW.TB_CACHE_VERSION WHERE CACHE_KEY = 'AUTH'");

    [Fact]
    public async Task 목록_부여현황과기능수()
    {
        await using var db = await BeginAsync(fixture);
        await db.ExecAsync("INSERT INTO FW.TB_USER_ROLE (USER_ID, ROLE_ID, START_DT, REG_ID, UPD_ID) VALUES ('it.rm.admin', 'IT_RM_ROLE', '2000-01-01', 'T', 'T')");

        var role = await RoleAsync(db, Role);
        var fresh = await RoleAsync(db, "PARTNER_USER");

        Assert.Equal((1, 0, "Y", 1, 900, "Y"), (role.UserCnt, role.DeptCnt, role.InUseYn, role.FuncCnt, role.SortNo, role.MyRoleYn));
        Assert.Equal("Y", fresh.SystemYn);
    }

    [Fact]
    public async Task 등록_감사로그_같은ID는중복()
    {
        await using var db = await BeginAsync(fixture);

        await CreateAsync(db, "IT_RM_NEW", "PARTNER", "COMPANY");

        var role = await RoleAsync(db, "IT_RM_NEW");
        Assert.Equal(("PARTNER", "COMPANY", "N", 910, Admin), (role.CompanyTypeCd, role.DataScopeCd, role.SystemYn, role.SortNo, role.UpdId));
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_ROLE' AND ACTION_CD = 'I' AND ROW_KEY = 'IT_RM_NEW'"));
        var ex = await Assert.ThrowsAsync<BizException>(() => CreateAsync(db, "IT_RM_NEW"));
        Assert.Equal(MessageKeys.DupCode, ex.MessageKey);
    }

    [Theory]
    [InlineData("it_rm_lower")]
    [InlineData("1IT_RM")]
    [InlineData("IT-RM")]
    [InlineData("I")]
    public async Task 등록_역할ID형식이틀리면거부(string roleId)
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => CreateAsync(db, roleId));

        Assert.Equal(MessageKeys.InvalidInput, ex.MessageKey);
    }

    [Fact]
    public async Task 수정_감사로그_권한캐시_ROW_VER()
    {
        await using var db = await BeginAsync(fixture);
        var before = await AuthVersionAsync(db);
        var oldVer = (await RoleAsync(db, Role)).RowVer;

        await UpdateAsync(db, Role, dataScopeCd: "DEPT_SUB", roleNm: "바뀐 이름");

        var role = await RoleAsync(db, Role);
        Assert.Equal(("바뀐 이름", "DEPT_SUB"), (role.RoleNm, role.DataScopeCd));
        Assert.NotEqual(oldVer, role.RowVer);
        Assert.Equal(before + 1, await AuthVersionAsync(db));
        var audit = await db.ScalarAsync<string>("SELECT TOP (1) CONCAT(JSON_VALUE(BEFORE_JSON, '$.DATA_SCOPE_CD'), '|', JSON_VALUE(AFTER_JSON, '$.DATA_SCOPE_CD')) FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_ROLE' AND ROW_KEY = @R ORDER BY LOG_ID DESC", ("@R", Role));
        Assert.Equal("SELF|DEPT_SUB", audit);
    }

    [Fact]
    public async Task 협력사역할은_데이터범위전체를못둔다()
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => CreateAsync(db, "IT_RM_PTN", "PARTNER", "ALL"));

        Assert.Equal(RoleMessageKeys.PartnerScope, ex.MessageKey);
    }

    [Fact]
    public async Task 시스템역할은_사용안함으로못바꾼다()
    {
        await using var db = await BeginAsync(fixture);
        await UpdateAsync(db, "PARTNER_USER", roleNm: "협력사 직원");

        var ex = await Assert.ThrowsAsync<BizException>(() => UpdateAsync(db, "PARTNER_USER", useYn: "N"));

        Assert.Equal(RoleMessageKeys.System, ex.MessageKey);
    }

    [Fact]
    public async Task 시스템역할은_회사유형을못바꾼다()
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => UpdateAsync(db, "EMP_USER", companyTypeCd: null));

        Assert.Equal(RoleMessageKeys.System, ex.MessageKey);
    }

    [Fact]
    public async Task 시스템관리자역할은_시스템관리자만고친다()
    {
        await using var db = await BeginAsync(fixture);
        await db.SeedUserAsync("it.rm.root", "HQ", "EMP", null, "SYS_ADMIN");
        db.AsUser("it.rm.root", "HQ", "EMP");
        await UpdateAsync(db, "SYS_ADMIN", roleNm: "시스템 관리자(비상)");
        db.AsUser(Admin, "HQ", "EMP");

        var ex = await Assert.ThrowsAsync<BizException>(() => UpdateAsync(db, "SYS_ADMIN", roleNm: "바꿈"));

        Assert.Equal(RoleMessageKeys.SysAdmin, ex.MessageKey);
    }

    [Fact]
    public async Task 시스템관리자도_시스템관리자역할의범위는못바꾼다()
    {
        await using var db = await BeginAsync(fixture);
        await db.SeedUserAsync("it.rm.root", "HQ", "EMP", null, "SYS_ADMIN");
        db.AsUser("it.rm.root", "HQ", "EMP");

        var ex = await Assert.ThrowsAsync<BizException>(() => UpdateAsync(db, "SYS_ADMIN", dataScopeCd: "COMPANY"));

        Assert.Equal(RoleMessageKeys.SysAdmin, ex.MessageKey);
    }

    [Fact]
    public async Task 내가가진역할은_못고친다()
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => UpdateAsync(db, "PORTAL_ADMIN", roleNm: "바꿈"));

        Assert.Equal(RoleMessageKeys.Self, ex.MessageKey);
    }

    [Fact]
    public async Task 기능권한이있거나부여된역할은_회사유형을못바꾼다_없으면바꾼다()
    {
        await using var db = await BeginAsync(fixture);
        await CreateAsync(db, "IT_RM_FREE", "HQ");
        await UpdateAsync(db, "IT_RM_FREE", companyTypeCd: "PARTNER");
        Assert.Equal("PARTNER", (await RoleAsync(db, "IT_RM_FREE")).CompanyTypeCd);

        var ex = await Assert.ThrowsAsync<BizException>(() => UpdateAsync(db, Role, companyTypeCd: "PARTNER"));

        Assert.Equal(RoleMessageKeys.TypeFixed, ex.MessageKey);
    }

    [Fact]
    public async Task 삭제_기능권한도함께_감사로그_권한캐시()
    {
        await using var db = await BeginAsync(fixture);
        var before = await AuthVersionAsync(db);

        Assert.Equal(2, await DeleteAsync(db, Role));

        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_ROLE WHERE ROLE_ID = 'IT_RM_ROLE'"));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_ROLE_PROGRAM_FUNC WHERE ROLE_ID = 'IT_RM_ROLE'"));
        Assert.Equal(before + 1, await AuthVersionAsync(db));
        var funcs = await db.ScalarAsync<string>("SELECT TOP (1) BEFORE_JSON FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_ROLE_PROGRAM_FUNC' AND ACTION_CD = 'D' AND ROW_KEY = 'IT_RM_ROLE' ORDER BY LOG_ID DESC");
        Assert.Equal("""["SYS_CODE_MGT:READ"]""", funcs);
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_ROLE' AND ACTION_CD = 'D' AND ROW_KEY = 'IT_RM_ROLE'"));
    }

    [Fact]
    public async Task 삭제_부서에부여된역할은거부()
    {
        await using var db = await BeginAsync(fixture);
        await db.ExecAsync("""
            INSERT INTO FW.TB_DEPT (DEPT_ID, COMPANY_CD, DEPT_NM) VALUES ('IT_RM_D', 'HQ', N'역할 테스트 부서');
            INSERT INTO FW.TB_DEPT_ROLE (DEPT_ID, ROLE_ID, START_DT, REG_ID, UPD_ID) VALUES ('IT_RM_D', 'IT_RM_ROLE', '2000-01-01', 'T', 'T');
            """);
        Assert.Equal(("Y", 1), ((await RoleAsync(db, Role)).InUseYn, (await RoleAsync(db, Role)).DeptCnt));

        var ex = await Assert.ThrowsAsync<BizException>(() => DeleteAsync(db, Role));

        Assert.Equal(RoleMessageKeys.InUse, ex.MessageKey);
    }

    [Fact]
    public async Task 삭제_시스템역할은거부()
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => DeleteAsync(db, "API_CLIENT"));

        Assert.Equal(RoleMessageKeys.System, ex.MessageKey);
    }

    [Fact]
    public async Task 다른사람이먼저고쳤으면_동시성오류()
    {
        await using var db = await BeginAsync(fixture);
        var stale = await RoleAsync(db, Role);
        await UpdateAsync(db, Role, roleNm: "먼저");

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync(RoleAdminService.SaveSp, Params(stale, roleNm: "나중"), Ct));

        Assert.Equal(MessageKeys.Concurrency, ex.MessageKey);
    }
}
