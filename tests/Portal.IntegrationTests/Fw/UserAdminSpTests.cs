using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.IntegrationTests.Infrastructure;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.IntegrationTests.Fw;

/// <summary>
/// 사용자 관리 SP: 목록, 역할 선택지(회사 유형·SYS_ADMIN), 저장(역할 더하고 빼기, 권한 캐시, 감사 로그),
/// 협력사 사용자에게 본사 역할 금지, 협력사 관리자는 자기 회사 사용자만(데이터 범위), 자기 계정 보호, SYS_ADMIN 보호, 동시성, 잠금 해제·2단계 인증 초기화.
/// </summary>
/// <remarks>SP 오류는 테스트 트랜잭션도 롤백하므로 오류 확인은 각 테스트의 마지막 호출로 둔다.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class UserAdminSpTests(DbFixture fixture)
{
    private const string Partner = "IT_UCO";
    private const string Admin = "it.u.admin";
    private const string PtnUser = "it.u.ptn";
    private const string EmpUser = "it.u.emp";
    private const string Partner2 = "IT_UCO2";
    private const string PtnAdmin = "it.u.padm";
    private const string PtnUser2 = "it.u.ptn2";
    private const string Sys = "SYS_USER_MGT";
    private const string Ptn = "PTN_USER_MGT";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>협력사 1곳, 협력사 사용자, 본사 사용자, 포털 관리자(현재 사용자)를 만든다.</summary>
    private static async Task<TestDb> BeginAsync(DbFixture fixture, string adminRole = "PORTAL_ADMIN")
    {
        var db = await fixture.BeginAsync();
        await db.SeedCompanyAsync(Partner);
        await db.SeedUserAsync(PtnUser, Partner, "PARTNER", null, "PARTNER_USER");
        await db.SeedUserAsync(EmpUser, "HQ", "EMP", null, "EMP_USER");
        await db.SeedUserAsync(Admin, "HQ", "EMP", null, adminRole);
        db.AsUser(Admin, "HQ", "EMP");
        return db;
    }

    /// <summary>협력사 한 곳을 더 만들고, 첫 협력사의 협력사 관리자로 바꾼다.</summary>
    private static async Task<TestDb> BeginPartnerAdminAsync(DbFixture fixture)
    {
        var db = await BeginAsync(fixture);
        await db.SeedCompanyAsync(Partner2);
        await db.SeedUserAsync(PtnUser2, Partner2, "PARTNER", null, "PARTNER_USER");
        await db.SeedUserAsync(PtnAdmin, Partner, "PARTNER", null, "PARTNER_ADMIN");
        db.AsUser(PtnAdmin, Partner, "PARTNER");
        return db;
    }

    private static async Task<UserAdminDto> GetAsync(TestDb db, string userId, string program = Sys)
    {
        var page = await db.Sp.QueryPageAsync<UserAdminDto>(UserAdminService.ListSp, new { P_PROGRAM_ID = program, P_KEYWORD = userId, P_PAGE_NO = 1, P_PAGE_SIZE = 50 }, Ct);
        return Assert.Single(page.Items, u => u.UserId == userId);
    }

    private static async Task<Portal.Data.SaveResult> SaveAsync(TestDb db, string userId, string[] roles, string stat = "ACTIVE", byte[]? rowVer = null, DateOnly? end = null, string program = Sys)
    {
        rowVer ??= (await GetAsync(db, userId, program)).RowVer;
        return await db.Sp.ExecuteAsync(
            UserAdminService.SaveSp,
            new { P_PROGRAM_ID = program, P_TARGET_USER_ID = userId, P_ROW_VER = rowVer, P_USER_NM = " 새 이름 ", P_USER_STAT_CD = stat, P_ACCOUNT_END_DT = end, P_ROLE_IDS = System.Text.Json.JsonSerializer.Serialize(roles) },
            Ct);
    }

    private static Task<IReadOnlyList<UserRoleDto>> RolesAsync(TestDb db, string userId, string program = Sys) =>
        db.Sp.QueryAsync<UserRoleDto>(UserAdminService.RoleListSp, new { P_PROGRAM_ID = program, P_TARGET_USER_ID = userId }, Ct);

    [Fact]
    public async Task 목록_회사로찾고_역할과2단계인증등록여부를준다()
    {
        await using var db = await BeginAsync(fixture);
        await db.ExecAsync("UPDATE FW.TB_USER SET MFA_SECRET_ENC = 0x01, LOCK_YN = 'Y', LOGIN_FAIL_CNT = 5 WHERE USER_ID = @U", ("@U", PtnUser));

        var page = await db.Sp.QueryPageAsync<UserAdminDto>(UserAdminService.ListSp, new { P_PROGRAM_ID = Sys, P_COMPANY_CD = Partner, P_PAGE_NO = 1, P_PAGE_SIZE = 50 }, Ct);
        var locked = await db.Sp.QueryPageAsync<UserAdminDto>(UserAdminService.ListSp, new { P_PROGRAM_ID = Sys, P_KEYWORD = "it.u.", P_LOCK_YN = "Y", P_PAGE_NO = 1, P_PAGE_SIZE = 50 }, Ct);

        var row = Assert.Single(page.Items);
        Assert.Equal((PtnUser, "PARTNER", "Y", "PARTNER_USER", "사용", 5), (row.UserId, row.CompanyTypeCd, row.MfaYn, row.RoleIds, row.UserStatNm, row.LoginFailCnt));
        Assert.Equal([PtnUser], locked.Items.Select(u => u.UserId));
    }

    [Fact]
    public async Task 역할선택지_협력사사용자에게는협력사역할만_SYS_ADMIN은잠김()
    {
        await using var db = await BeginAsync(fixture);

        var partner = (await RolesAsync(db, PtnUser)).ToDictionary(r => r.RoleId);
        var emp = (await RolesAsync(db, EmpUser)).ToDictionary(r => r.RoleId);

        Assert.Equal(("Y", "Y", "Y"), (partner["PARTNER_USER"].AssignedYn, partner["PARTNER_USER"].EffectiveYn, partner["PARTNER_ADMIN"].AssignableYn));
        Assert.Equal("N", partner["PORTAL_ADMIN"].AssignableYn);
        Assert.Equal(("Y", "N", "N"), (emp["PORTAL_ADMIN"].AssignableYn, emp["PARTNER_USER"].AssignableYn, emp["SYS_ADMIN"].AssignableYn));
    }

    [Fact]
    public async Task 역할선택지_시스템관리자는SYS_ADMIN을줄수있다()
    {
        await using var db = await BeginAsync(fixture, adminRole: "SYS_ADMIN");

        Assert.Equal("Y", (await RolesAsync(db, EmpUser)).Single(r => r.RoleId == "SYS_ADMIN").AssignableYn);
    }

    [Fact]
    public async Task 저장_역할을더하고빼고_권한캐시를올리고_감사로그를남긴다()
    {
        await using var db = await BeginAsync(fixture);
        var cacheBefore = await db.ScalarAsync<long>("SELECT VERSION_NO FROM FW.TB_CACHE_VERSION WHERE CACHE_KEY = 'AUTH'");

        await SaveAsync(db, PtnUser, ["PARTNER_ADMIN"], end: new DateOnly(2030, 1, 31));

        var row = await GetAsync(db, PtnUser);
        Assert.Equal(("새 이름", "PARTNER_ADMIN", new DateOnly(2030, 1, 31)), (row.UserNm, row.RoleIds, row.AccountEndDt));
        Assert.Equal(cacheBefore + 1, await db.ScalarAsync<long>("SELECT VERSION_NO FROM FW.TB_CACHE_VERSION WHERE CACHE_KEY = 'AUTH'"));
        var audit = await db.ScalarAsync<string>("SELECT TOP 1 CONCAT(BEFORE_JSON, '>', AFTER_JSON) FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_USER' AND ROW_KEY = @U ORDER BY LOG_ID DESC", ("@U", PtnUser));
        Assert.Contains("\"ROLE_IDS\":\"PARTNER_USER\"", audit, StringComparison.Ordinal);
        Assert.Contains("\"ROLE_IDS\":\"PARTNER_ADMIN\"", audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 저장_이름만바꾸면_권한캐시는그대로()
    {
        await using var db = await BeginAsync(fixture);
        var cacheBefore = await db.ScalarAsync<long>("SELECT VERSION_NO FROM FW.TB_CACHE_VERSION WHERE CACHE_KEY = 'AUTH'");

        await SaveAsync(db, PtnUser, ["PARTNER_USER"]);

        Assert.Equal(cacheBefore, await db.ScalarAsync<long>("SELECT VERSION_NO FROM FW.TB_CACHE_VERSION WHERE CACHE_KEY = 'AUTH'"));
    }

    [Fact]
    public async Task 저장_잘못들어간본사역할은_협력사사용자에게서뺄수있다()
    {
        await using var db = await BeginAsync(fixture);
        await db.ExecAsync("INSERT INTO FW.TB_USER_ROLE (USER_ID, ROLE_ID) VALUES (@U, 'PORTAL_ADMIN')", ("@U", PtnUser));

        await SaveAsync(db, PtnUser, ["PARTNER_USER"]);

        Assert.Equal("PARTNER_USER", (await GetAsync(db, PtnUser)).RoleIds);
    }

    [Fact]
    public async Task 저장_협력사사용자에게본사역할은거부()
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, PtnUser, ["PARTNER_USER", "PORTAL_ADMIN"]));
        Assert.Equal(UserAdminMessageKeys.RoleType, ex.MessageKey);
    }

    [Fact]
    public async Task 저장_본사사용자에게협력사역할은거부()
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, EmpUser, ["EMP_USER", "PARTNER_ADMIN"]));
        Assert.Equal(UserAdminMessageKeys.RoleType, ex.MessageKey);
    }

    [Fact]
    public async Task 저장_자기상태나역할은못바꾼다()
    {
        await using var db = await BeginAsync(fixture);
        await SaveAsync(db, Admin, ["PORTAL_ADMIN"]); // 이름만은 된다

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, Admin, ["PORTAL_ADMIN", "SECURITY_AUDITOR"]));
        Assert.Equal(UserAdminMessageKeys.SelfChange, ex.MessageKey);
    }

    [Fact]
    public async Task 저장_포털관리자는SYS_ADMIN을줄수없다()
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, EmpUser, ["EMP_USER", "SYS_ADMIN"]));
        Assert.Equal(UserAdminMessageKeys.SysAdminOnly, ex.MessageKey);
    }

    [Fact]
    public async Task 저장_포털관리자는SYS_ADMIN사용자를고칠수없다()
    {
        await using var db = await BeginAsync(fixture);
        await db.ExecAsync("INSERT INTO FW.TB_USER_ROLE (USER_ID, ROLE_ID) VALUES (@U, 'SYS_ADMIN')", ("@U", EmpUser));

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, EmpUser, ["EMP_USER", "SYS_ADMIN"], stat: "LOCKED"));
        Assert.Equal(UserAdminMessageKeys.SysAdminOnly, ex.MessageKey);
    }

    [Fact]
    public async Task 저장_시스템관리자는SYS_ADMIN을준다()
    {
        await using var db = await BeginAsync(fixture, adminRole: "SYS_ADMIN");

        await SaveAsync(db, EmpUser, ["EMP_USER", "SYS_ADMIN"]);

        Assert.Equal("SYS_ADMIN,EMP_USER", (await GetAsync(db, EmpUser)).RoleIds);
    }

    [Fact]
    public async Task 저장_사용중지하면_다음세션확인에서끊긴다()
    {
        await using var db = await BeginAsync(fixture);
        await SaveAsync(db, PtnUser, ["PARTNER_USER"], stat: "RETIRED");

        Assert.Equal("RETIRED", (await GetAsync(db, PtnUser)).UserStatCd);
        Assert.Empty(await db.ColumnAsync<string>("SELECT ROLE_ID FROM FW.UFN_USER_ROLES(@U)", ("@U", PtnUser)));
    }

    [Fact]
    public async Task 저장_승인대기같은시스템상태로는바꿀수없다()
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, PtnUser, ["PARTNER_USER"], stat: "PENDING"));
        Assert.Equal(MessageKeys.InvalidInput, ex.MessageKey);
    }

    [Fact]
    public async Task 저장_다른사람이먼저고쳤으면거부()
    {
        await using var db = await BeginAsync(fixture);
        var stale = (await GetAsync(db, PtnUser)).RowVer;
        await SaveAsync(db, PtnUser, ["PARTNER_USER"], rowVer: stale);

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, PtnUser, ["PARTNER_USER"], rowVer: stale));
        Assert.Equal(MessageKeys.Concurrency, ex.MessageKey);
    }

    [Fact]
    public async Task 초기화_잠금해제와2단계인증초기화_두번째는바꿀것없음()
    {
        await using var db = await BeginAsync(fixture);
        await db.ExecAsync("UPDATE FW.TB_USER SET MFA_SECRET_ENC = 0x01, MFA_LAST_STEP = 99, LOCK_YN = 'Y', LOCK_DT = SYSUTCDATETIME(), LOGIN_FAIL_CNT = 5 WHERE USER_ID = @U", ("@U", PtnUser));

        var unlock = await db.Sp.ExecuteAsync(UserAdminService.ResetSp, new { P_PROGRAM_ID = Sys, P_TARGET_USER_ID = PtnUser, P_ACTION_CD = UserResetActions.Unlock }, Ct);
        var mfa = await db.Sp.ExecuteAsync(UserAdminService.ResetSp, new { P_PROGRAM_ID = Sys, P_TARGET_USER_ID = PtnUser, P_ACTION_CD = UserResetActions.MfaReset }, Ct);
        var again = await db.Sp.ExecuteAsync(UserAdminService.ResetSp, new { P_PROGRAM_ID = Sys, P_TARGET_USER_ID = PtnUser, P_ACTION_CD = UserResetActions.MfaReset }, Ct);

        Assert.Equal((1, 1, 0), (unlock.AffectedCnt, mfa.AffectedCnt, again.AffectedCnt));
        Assert.Equal("N|0|-|-", await db.ScalarAsync<string>("SELECT CONCAT(LOCK_YN, '|', LOGIN_FAIL_CNT, '|', ISNULL(CAST(MFA_LAST_STEP AS VARCHAR), '-'), '|', IIF(MFA_SECRET_ENC IS NULL, '-', 'x')) FROM FW.TB_USER WHERE USER_ID = @U", ("@U", PtnUser)));
        Assert.Equal(2, await db.ScalarAsync<int>("SELECT COUNT(*) FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_USER' AND ROW_KEY = @U AND USER_ID = @A", ("@U", PtnUser), ("@A", Admin)));
    }

    [Fact]
    public async Task 초기화_자기계정은거부()
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync(UserAdminService.ResetSp, new { P_PROGRAM_ID = Sys, P_TARGET_USER_ID = Admin, P_ACTION_CD = UserResetActions.MfaReset }, Ct));
        Assert.Equal(UserAdminMessageKeys.SelfChange, ex.MessageKey);
    }

    [Fact]
    public async Task 초기화_포털관리자는SYS_ADMIN사용자를초기화할수없다()
    {
        await using var db = await BeginAsync(fixture);
        await db.ExecAsync("INSERT INTO FW.TB_USER_ROLE (USER_ID, ROLE_ID) VALUES (@U, 'SYS_ADMIN')", ("@U", EmpUser));

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync(UserAdminService.ResetSp, new { P_PROGRAM_ID = Sys, P_TARGET_USER_ID = EmpUser, P_ACTION_CD = UserResetActions.MfaReset }, Ct));
        Assert.Equal(UserAdminMessageKeys.SysAdminOnly, ex.MessageKey);
    }

    [Fact]
    public async Task 프로그램ID가_사용자관리화면이아니면거부()
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.QueryPageAsync<UserAdminDto>(UserAdminService.ListSp, new { P_PROGRAM_ID = "SYS_CODE_MGT", P_PAGE_NO = 1, P_PAGE_SIZE = 50 }, Ct));
        Assert.Equal(MessageKeys.InvalidInput, ex.MessageKey);
    }

    [Fact]
    public async Task 협력사관리자_목록은자기회사사용자만_본사화면으로는아무도안보인다()
    {
        await using var db = await BeginPartnerAdminAsync(fixture);

        var own = await db.Sp.QueryPageAsync<UserAdminDto>(UserAdminService.ListSp, new { P_PROGRAM_ID = Ptn, P_KEYWORD = "it.u.", P_PAGE_NO = 1, P_PAGE_SIZE = 50 }, Ct);
        var other = await db.Sp.QueryPageAsync<UserAdminDto>(UserAdminService.ListSp, new { P_PROGRAM_ID = Ptn, P_COMPANY_CD = Partner2, P_PAGE_NO = 1, P_PAGE_SIZE = 50 }, Ct);
        var sys = await db.Sp.QueryPageAsync<UserAdminDto>(UserAdminService.ListSp, new { P_PROGRAM_ID = Sys, P_KEYWORD = "it.u.", P_PAGE_NO = 1, P_PAGE_SIZE = 50 }, Ct);

        Assert.Equal([PtnAdmin, PtnUser], own.Items.Select(u => u.UserId).Order(StringComparer.Ordinal));
        Assert.Equal(2, own.TotalCount);
        Assert.Equal((0, 0), (other.Items.Count, other.TotalCount));
        Assert.Equal((0, 0), (sys.Items.Count, sys.TotalCount));
    }

    [Fact]
    public async Task 협력사관리자_자기회사사용자에게협력사역할만보이고줄수있다()
    {
        await using var db = await BeginPartnerAdminAsync(fixture);

        var roles = (await RolesAsync(db, PtnUser, Ptn)).ToDictionary(r => r.RoleId);
        await SaveAsync(db, PtnUser, ["PARTNER_ADMIN", "PARTNER_USER"], program: Ptn);

        Assert.Equal(("Y", "Y"), (roles["PARTNER_ADMIN"].AssignableYn, roles["PARTNER_USER"].AssignableYn));
        Assert.Equal(["PARTNER_ADMIN", "PARTNER_USER"], roles.Keys.Order(StringComparer.Ordinal)); // 본사 역할은 목록에도 없다
        Assert.Equal("PARTNER_ADMIN,PARTNER_USER", (await GetAsync(db, PtnUser, Ptn)).RoleIds);
    }

    [Fact]
    public async Task 협력사관리자_다른회사사용자의역할은없는것으로()
    {
        await using var db = await BeginPartnerAdminAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => RolesAsync(db, PtnUser2, Ptn));
        Assert.Equal(MessageKeys.NotFound, ex.MessageKey);
    }

    [Fact]
    public async Task 협력사관리자_다른회사사용자저장은없는것으로()
    {
        await using var db = await BeginPartnerAdminAsync(fixture);
        var rowVer = await db.ScalarAsync<byte[]>("SELECT ROW_VER FROM FW.TB_USER WHERE USER_ID = @U", ("@U", PtnUser2));

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, PtnUser2, ["PARTNER_USER"], stat: "RETIRED", rowVer: rowVer, program: Ptn));
        Assert.Equal(MessageKeys.NotFound, ex.MessageKey);
    }

    [Fact]
    public async Task 협력사관리자_본사사용자초기화는없는것으로()
    {
        await using var db = await BeginPartnerAdminAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync(UserAdminService.ResetSp, new { P_PROGRAM_ID = Ptn, P_TARGET_USER_ID = EmpUser, P_ACTION_CD = UserResetActions.Unlock }, Ct));
        Assert.Equal(MessageKeys.NotFound, ex.MessageKey);
    }

    [Fact]
    public async Task 협력사관리자_자기회사사용자잠금해제()
    {
        await using var db = await BeginPartnerAdminAsync(fixture);
        await db.ExecAsync("UPDATE FW.TB_USER SET LOCK_YN = 'Y', LOGIN_FAIL_CNT = 5 WHERE USER_ID = @U", ("@U", PtnUser));

        var result = await db.Sp.ExecuteAsync(UserAdminService.ResetSp, new { P_PROGRAM_ID = Ptn, P_TARGET_USER_ID = PtnUser, P_ACTION_CD = UserResetActions.Unlock }, Ct);

        Assert.Equal(1, result.AffectedCnt);
        Assert.Equal("N", await db.ScalarAsync<string>("SELECT LOCK_YN FROM FW.TB_USER WHERE USER_ID = @U", ("@U", PtnUser)));
    }

    [Fact]
    public async Task 협력사관리자_협력사화면에서도본사역할은거부()
    {
        await using var db = await BeginPartnerAdminAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, PtnUser, ["PARTNER_USER", "EMP_USER"], program: Ptn));
        Assert.Equal(UserAdminMessageKeys.RoleType, ex.MessageKey);
    }
}
