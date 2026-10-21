using Portal.IntegrationTests.Infrastructure;

namespace Portal.IntegrationTests.Fw;

/// <summary>FW.UFN_USER_ROLES / FW.UFN_DATA_SCOPE: 유효 역할과 회사 간 데이터 격리(설계서 6장).</summary>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class DataScopeTests(DbFixture fixture)
{
    private const string Program = "PTN_USER_MGT";

    [Fact]
    public async Task 협력사관리자_소속회사만()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedCompanyAsync("P9001");
        await db.SeedCompanyAsync("P9002");
        await db.SeedUserAsync("p1.admin", "P9001", "PARTNER", roles: ["PARTNER_ADMIN"]);

        var scope = await Scope(db, "p1.admin", Program);

        Assert.Equal(["P9001||"], scope);
    }

    [Fact]
    public async Task 협력사사용자에게전체범위역할을줘도_소속회사로제한()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedCompanyAsync("P9001");
        await db.SeedCompanyAsync("P9002");
        await db.SeedUserAsync("p1.mis", "P9001", "PARTNER", roles: ["PORTAL_ADMIN", "PARTNER_ADMIN"]);

        var scope = await Scope(db, "p1.mis", Program);

        Assert.Equal(["P9001||"], scope);
    }

    [Fact]
    public async Task 시스템관리자_모든회사()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedCompanyAsync("P9001");

        var scope = await Scope(db, "it.admin", Program);

        Assert.Contains("HQ||", scope);
        Assert.Contains("P9001||", scope);
    }

    [Fact]
    public async Task 조회권한없음_0행()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedUserAsync("emp.plain", "HQ", "EMP", roles: ["EMP_USER"]);

        Assert.Empty(await Scope(db, "emp.plain", Program));
    }

    [Fact]
    public async Task 본인범위_사용자ID조건()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedUserAsync("emp.self", "HQ", "EMP", roles: ["EMP_USER"]);

        Assert.Equal(["HQ||emp.self"], await Scope(db, "emp.self", "DSH_HOME"));
    }

    [Fact]
    public async Task 부서와하위범위_하위부서모두()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedDeptAsync("D1", "HQ");
        await db.SeedDeptAsync("D11", "HQ", "D1");
        await db.SeedDeptAsync("D111", "HQ", "D11");
        await db.SeedDeptAsync("D2", "HQ");
        await db.ExecAsync("INSERT INTO FW.TB_ROLE (ROLE_ID, ROLE_NM, DATA_SCOPE_CD) VALUES ('IT_DEPT_SUB', 'x', 'DEPT_SUB')");
        await db.ExecAsync("INSERT INTO FW.TB_ROLE_PROGRAM_FUNC (ROLE_ID, PROGRAM_ID, FUNC_CD) VALUES ('IT_DEPT_SUB', 'DSH_HOME', 'READ')");
        await db.SeedUserAsync("emp.mgr", "HQ", "EMP", "D1", "IT_DEPT_SUB");

        var scope = await Scope(db, "emp.mgr", "DSH_HOME");

        Assert.Equal(new[] { "HQ|D1|", "HQ|D11|", "HQ|D111|" }.Order(StringComparer.Ordinal), scope.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task 부서역할상속과AD그룹역할과위임_유효역할에포함()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedDeptAsync("D1", "HQ");
        await db.SeedDeptAsync("D11", "HQ", "D1");
        await db.SeedUserAsync("emp.x", "HQ", "EMP", "D11");
        await db.ExecAsync("INSERT INTO FW.TB_DEPT_ROLE (DEPT_ID, ROLE_ID, INHERIT_YN) VALUES ('D1', 'EMP_USER', 'Y'), ('D1', 'SECURITY_AUDITOR', 'N')");
        await db.ExecAsync("INSERT INTO FW.TB_AD_GROUP_ROLE (AD_GROUP_SID, ROLE_ID) VALUES ('S-1-5-IT', 'PORTAL_ADMIN')");
        await db.ExecAsync("INSERT INTO FW.TB_USER_AD_GROUP (USER_ID, AD_GROUP_SID) VALUES ('emp.x', 'S-1-5-IT')");
        await db.ExecAsync("INSERT INTO FW.TB_ROLE_DELEGATE (FROM_USER_ID, TO_USER_ID, ROLE_ID, START_DT, END_DT) VALUES ('it.admin', 'emp.x', 'PARTNER_ADMIN', DATEADD(DAY, -1, GETDATE()), DATEADD(DAY, 1, GETDATE()))");
        await db.ExecAsync("INSERT INTO FW.TB_USER_ROLE (USER_ID, ROLE_ID, START_DT, END_DT) VALUES ('emp.x', 'API_CLIENT', '2000-01-01', '2000-12-31')");

        var roles = await db.ColumnAsync<string>("SELECT ROLE_ID FROM FW.UFN_USER_ROLES('emp.x') ORDER BY ROLE_ID");

        // 상속 안 되는 상위 부서 역할(SECURITY_AUDITOR)과 기간 지난 역할(API_CLIENT)은 빠진다
        Assert.Equal(["EMP_USER", "PARTNER_ADMIN", "PORTAL_ADMIN"], roles);
    }

    [Fact]
    public async Task 잠긴사용자_역할없음()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedUserAsync("emp.locked", "HQ", "EMP", roles: ["PORTAL_ADMIN"]);
        await db.ExecAsync("UPDATE FW.TB_USER SET USER_STAT_CD = 'LOCKED' WHERE USER_ID = 'emp.locked'");

        Assert.Empty(await Scope(db, "emp.locked", "SYS_USER_MGT"));
    }

    private static Task<List<string>> Scope(TestDb db, string userId, string programId) =>
        db.ColumnAsync<string>(
            "SELECT CONCAT(COMPANY_CD, '|', DEPT_ID, '|', USER_ID) FROM FW.UFN_DATA_SCOPE(@U, @P)",
            ("@U", userId), ("@P", programId));
}
