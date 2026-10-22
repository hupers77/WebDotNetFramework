using System.Text.Json;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.IntegrationTests.Infrastructure;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.IntegrationTests.Fw;

/// <summary>
/// 부서 관리 SP: 트리 목록(사용 안 함 제외·포함), 등록(ID 자동 부여), 상위 부서 규칙(같은 회사, 순환 금지, 사용 안 함 아래 금지),
/// 하위가 사용 중이면 사용 안 함 금지, 부서 역할(회사 유형, SYS_ADMIN 금지, 상속), 권한 캐시·감사 로그, AD 부서 보호, 삭제 조건,
/// 사용 안 함 부서는 역할을 주지 않는다.
/// </summary>
/// <remarks>SP 오류는 테스트 트랜잭션도 롤백하므로 오류 확인은 각 테스트의 마지막 호출로 둔다.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class DeptAdminSpTests(DbFixture fixture)
{
    private const string Partner = "IT_DCO";
    private const string Admin = "it.d.admin";
    private const string Root = "IT_D_ROOT";
    private const string Child = "IT_D_CHILD";
    private const string Grand = "IT_D_GRAND";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>본사에 부서 3단(ROOT > CHILD > GRAND), 협력사 1곳, 포털 관리자(현재 사용자)를 만든다.</summary>
    private static async Task<TestDb> BeginAsync(DbFixture fixture)
    {
        var db = await fixture.BeginAsync();
        await db.SeedCompanyAsync(Partner);
        await db.SeedDeptAsync(Root, "HQ");
        await db.SeedDeptAsync(Child, "HQ", Root);
        await db.SeedDeptAsync(Grand, "HQ", Child);
        await db.SeedUserAsync(Admin, "HQ", "EMP", null, "PORTAL_ADMIN");
        db.AsUser(Admin, "HQ", "EMP");
        return db;
    }

    private static Task<IReadOnlyList<DeptDto>> ListAsync(TestDb db, string company = "HQ", string unused = "N") =>
        db.Sp.QueryAsync<DeptDto>(DeptAdminService.ListSp, new { P_COMPANY_CD = company, P_INCLUDE_UNUSED_YN = unused }, Ct);

    private static async Task<DeptDto> GetAsync(TestDb db, string deptId, string company = "HQ") =>
        Assert.Single(await ListAsync(db, company, "Y"), d => d.DeptId == deptId);

    private static async Task<Portal.Data.SaveResult> SaveAsync(
        TestDb db, string? deptId, string? parent, string name = "새 부서", string use = "Y", object[]? roles = null, string company = "HQ")
    {
        var rowVer = deptId is null ? null : (await GetAsync(db, deptId, company)).RowVer;
        return await db.Sp.ExecuteAsync(
            DeptAdminService.SaveSp,
            new { P_DEPT_ID = deptId, P_ROW_VER = rowVer, P_COMPANY_CD = company, P_PARENT_DEPT_ID = parent, P_DEPT_NM = name, P_SORT_NO = 5, P_USE_YN = use, P_ROLES = JsonSerializer.Serialize(roles ?? []) },
            Ct);
    }

    private static object Role(string roleId, string inherit = "Y") => new { roleId, inheritYn = inherit };

    private static Task<long> AuthVersionAsync(TestDb db) => db.ScalarAsync<long>("SELECT VERSION_NO FROM FW.TB_CACHE_VERSION WHERE CACHE_KEY = 'AUTH'");

    [Fact]
    public async Task 목록_상위순서로주고_사용안함은기본제외_하위도함께빠진다()
    {
        await using var db = await BeginAsync(fixture);
        await db.SeedUserAsync("it.d.u1", "HQ", "EMP", Child);
        await db.ExecAsync("UPDATE FW.TB_DEPT SET USE_YN = 'N' WHERE DEPT_ID = @D", ("@D", Child));

        var used = (await ListAsync(db)).Where(d => d.DeptId.StartsWith("IT_D_", StringComparison.Ordinal)).ToList();
        var all = (await ListAsync(db, unused: "Y")).Where(d => d.DeptId.StartsWith("IT_D_", StringComparison.Ordinal)).ToList();

        Assert.Equal([Root], used.Select(d => d.DeptId));
        Assert.Equal([Root, Child, Grand], all.Select(d => d.DeptId));
        var child = all.Single(d => d.DeptId == Child);
        Assert.Equal((Root, 1, 1, "N", "N"), (child.ParentDeptId, child.UserCnt, child.ChildCnt, child.UseYn, child.AdYn));
    }

    [Fact]
    public async Task 목록_권한없는사용자에게는0행()
    {
        await using var db = await BeginAsync(fixture);
        await db.SeedUserAsync("it.d.nobody", "HQ", "EMP", null, "EMP_USER");
        db.AsUser("it.d.nobody", "HQ", "EMP");

        Assert.Empty(await ListAsync(db));
    }

    [Fact]
    public async Task 등록_ID를차례로부여하고_역할을주고_권한캐시와감사로그()
    {
        await using var db = await BeginAsync(fixture);
        var before = await AuthVersionAsync(db);
        var lastLog = await db.ScalarAsync<long>("SELECT ISNULL(MAX(LOG_ID), 0) FROM LOG.TB_AUDIT_LOG");

        var first = await SaveAsync(db, null, Root, roles: [Role("EMP_USER", "N")]);
        var second = await SaveAsync(db, null, null);

        Assert.Matches("^D[0-9]{6}$", first.NewKey);
        Assert.Equal(int.Parse(first.NewKey![1..], System.Globalization.CultureInfo.InvariantCulture) + 1, int.Parse(second.NewKey![1..], System.Globalization.CultureInfo.InvariantCulture));
        var row = await GetAsync(db, first.NewKey);
        Assert.Equal((Root, "새 부서", "EMP_USER"), (row.ParentDeptId, row.DeptNm, row.RoleIds));
        Assert.Equal("N", await db.ScalarAsync<string>("SELECT INHERIT_YN FROM FW.TB_DEPT_ROLE WHERE DEPT_ID = @D", ("@D", first.NewKey)));
        Assert.Equal(before + 1, await AuthVersionAsync(db)); // 역할이 없는 두 번째 등록은 캐시를 올리지 않는다
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_DEPT' AND ROW_KEY = @D AND ACTION_CD = 'I' AND LOG_ID > @L", ("@D", first.NewKey), ("@L", lastLog)));
    }

    [Fact]
    public async Task 수정_이름만바꾸면권한캐시그대로_상위를옮기면올린다()
    {
        await using var db = await BeginAsync(fixture);
        var before = await AuthVersionAsync(db);

        await SaveAsync(db, Grand, Child, name: "이름 변경");
        Assert.Equal(before, await AuthVersionAsync(db));
        await SaveAsync(db, Grand, Root);

        Assert.Equal(Root, (await GetAsync(db, Grand)).ParentDeptId);
        Assert.Equal(before + 1, await AuthVersionAsync(db));
    }

    [Fact]
    public async Task 역할_상속여부를고치고빼고_감사로그에전후가남는다()
    {
        await using var db = await BeginAsync(fixture);
        await SaveAsync(db, Root, null, roles: [Role("EMP_USER"), Role("SECURITY_AUDITOR")]);

        await SaveAsync(db, Root, null, roles: [Role("EMP_USER", "N")]);

        Assert.Equal(["EMP_USER:N"], await db.ColumnAsync<string>("SELECT ROLE_ID + ':' + INHERIT_YN FROM FW.TB_DEPT_ROLE WHERE DEPT_ID = @D", ("@D", Root)));
        var audit = await db.ScalarAsync<string>("SELECT TOP 1 CONCAT(BEFORE_JSON, '>', AFTER_JSON) FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_DEPT' AND ROW_KEY = @D ORDER BY LOG_ID DESC", ("@D", Root));
        Assert.Contains("SECURITY_AUDITOR", audit, StringComparison.Ordinal);
        Assert.Contains("\"INHERIT_YN\":\"N\"", audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 사용안함부서는_소속계정에역할을주지않는다()
    {
        await using var db = await BeginAsync(fixture);
        await db.SeedUserAsync("it.d.member", "HQ", "EMP", Grand);
        await SaveAsync(db, Root, null, roles: [Role("SECURITY_AUDITOR")]);
        var inherited = await db.ColumnAsync<string>("SELECT ROLE_ID FROM FW.UFN_USER_ROLES('it.d.member')");

        await db.ExecAsync("UPDATE FW.TB_DEPT SET USE_YN = 'N' WHERE DEPT_ID = @D", ("@D", Root));

        Assert.Contains("SECURITY_AUDITOR", inherited);
        Assert.DoesNotContain("SECURITY_AUDITOR", await db.ColumnAsync<string>("SELECT ROLE_ID FROM FW.UFN_USER_ROLES('it.d.member')"));
    }

    [Fact]
    public async Task 역할_협력사부서에본사역할은거부()
    {
        await using var db = await BeginAsync(fixture);
        var dept = (await SaveAsync(db, null, null, company: Partner, roles: [Role("PARTNER_USER")])).NewKey;

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, dept, null, company: Partner, roles: [Role("PARTNER_USER"), Role("EMP_USER")]));
        Assert.Equal(DeptMessageKeys.RoleType, ex.MessageKey);
    }

    [Fact]
    public async Task 역할_SYS_ADMIN은부서로줄수없다()
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, Root, null, roles: [Role("SYS_ADMIN")]));
        Assert.Equal(DeptMessageKeys.RoleType, ex.MessageKey);
    }

    [Fact]
    public async Task 역할선택지_부서회사유형과SYS_ADMIN()
    {
        await using var db = await BeginAsync(fixture);
        await db.ExecAsync("INSERT INTO FW.TB_DEPT_ROLE (DEPT_ID, ROLE_ID, INHERIT_YN) VALUES (@D, 'EMP_USER', 'N')", ("@D", Root));

        var roles = (await db.Sp.QueryAsync<DeptRoleDto>(DeptAdminService.RoleListSp, new { P_DEPT_ID = Root }, Ct)).ToDictionary(r => r.RoleId);

        Assert.Equal(("Y", "N", "Y"), (roles["EMP_USER"].AssignedYn, roles["EMP_USER"].InheritYn, roles["EMP_USER"].AssignableYn));
        Assert.Equal(("N", "N"), (roles["PARTNER_USER"].AssignableYn, roles["SYS_ADMIN"].AssignableYn));
    }

    [Fact]
    public async Task 상위_자기하위로옮기면거부()
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, Root, Grand));
        Assert.Equal(DeptMessageKeys.ParentCycle, ex.MessageKey);
    }

    [Fact]
    public async Task 상위_자기자신이면거부()
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, Child, Child));
        Assert.Equal(DeptMessageKeys.ParentCycle, ex.MessageKey);
    }

    [Fact]
    public async Task 상위_다른회사부서는거부()
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, null, Root, company: Partner));
        Assert.Equal(MessageKeys.InvalidInput, ex.MessageKey);
    }

    [Fact]
    public async Task 상위_사용안함부서아래에사용중부서는거부()
    {
        await using var db = await BeginAsync(fixture);
        await db.ExecAsync("UPDATE FW.TB_DEPT SET USE_YN = 'N' WHERE DEPT_ID = @D", ("@D", Grand));

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, null, Grand));
        Assert.Equal(DeptMessageKeys.ParentUnused, ex.MessageKey);
    }

    [Fact]
    public async Task 사용안함_사용중인하위가있으면거부()
    {
        await using var db = await BeginAsync(fixture);

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, Child, Root, use: "N"));
        Assert.Equal(DeptMessageKeys.ChildInUse, ex.MessageKey);
    }

    [Fact]
    public async Task AD부서_이름은못바꾸고_역할과정렬은된다()
    {
        await using var db = await BeginAsync(fixture);
        await db.ExecAsync("UPDATE FW.TB_DEPT SET AD_OU_PATH = N'OU=IT,DC=corp' WHERE DEPT_ID = @D", ("@D", Grand));
        var name = (await GetAsync(db, Grand)).DeptNm;

        await SaveAsync(db, Grand, Child, name: name, roles: [Role("EMP_USER")]);
        Assert.Equal(("EMP_USER", 5, "Y"), ((await GetAsync(db, Grand)).RoleIds, (await GetAsync(db, Grand)).SortNo, (await GetAsync(db, Grand)).AdYn));

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, Grand, Child, name: "바꾼 이름"));
        Assert.Equal(DeptMessageKeys.AdManaged, ex.MessageKey);
    }

    [Fact]
    public async Task 수정_다른사람이먼저고쳤으면거부()
    {
        await using var db = await BeginAsync(fixture);
        var stale = (await GetAsync(db, Grand)).RowVer;
        await SaveAsync(db, Grand, Child, name: "먼저");

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync(
            DeptAdminService.SaveSp,
            new { P_DEPT_ID = Grand, P_ROW_VER = stale, P_COMPANY_CD = "HQ", P_PARENT_DEPT_ID = Child, P_DEPT_NM = "나중", P_SORT_NO = 0, P_USE_YN = "Y", P_ROLES = "[]" },
            Ct));
        Assert.Equal(MessageKeys.Concurrency, ex.MessageKey);
    }

    [Fact]
    public async Task 삭제_빈부서는지우고역할도함께_감사로그()
    {
        await using var db = await BeginAsync(fixture);
        await SaveAsync(db, Grand, Child, roles: [Role("EMP_USER")]);
        var rowVer = (await GetAsync(db, Grand)).RowVer;

        await db.Sp.ExecuteAsync(DeptAdminService.DeleteSp, new { P_DEPT_ID = Grand, P_ROW_VER = rowVer }, Ct);

        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_DEPT WHERE DEPT_ID = @D", ("@D", Grand)));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_DEPT_ROLE WHERE DEPT_ID = @D", ("@D", Grand)));
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_DEPT' AND ROW_KEY = @D AND ACTION_CD = 'D'", ("@D", Grand)));
    }

    [Fact]
    public async Task 삭제_하위부서가있으면거부()
    {
        await using var db = await BeginAsync(fixture);
        var rowVer = (await GetAsync(db, Child)).RowVer;

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync(DeptAdminService.DeleteSp, new { P_DEPT_ID = Child, P_ROW_VER = rowVer }, Ct));
        Assert.Equal(DeptMessageKeys.InUse, ex.MessageKey);
    }

    [Fact]
    public async Task 삭제_퇴사자라도소속계정이있으면거부()
    {
        await using var db = await BeginAsync(fixture);
        await db.SeedUserAsync("it.d.left", "HQ", "EMP", Grand);
        await db.ExecAsync("UPDATE FW.TB_USER SET USER_STAT_CD = 'RETIRED' WHERE USER_ID = 'it.d.left'");
        var rowVer = (await GetAsync(db, Grand)).RowVer;

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync(DeptAdminService.DeleteSp, new { P_DEPT_ID = Grand, P_ROW_VER = rowVer }, Ct));
        Assert.Equal(DeptMessageKeys.InUse, ex.MessageKey);
    }
}
