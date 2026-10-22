using System.Text.Json;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.IntegrationTests.Infrastructure;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.IntegrationTests.Fw;

/// <summary>
/// 사용자별 예외 권한 SP: 대상 정보·기능별 역할 허용 여부, 통째 저장(추가·변경·삭제·변경 없음), 감사 로그·권한 캐시·버전,
/// 데이터 범위 밖 대상, 자기 자신·시스템 관리자 보호, 종료일, 회사 유형에 열리지 않은 화면, 사용 안 함 프로그램 보존, 입력 검사, 동시성.
/// </summary>
/// <remarks>SP 오류는 테스트 트랜잭션도 롤백하므로 오류 확인은 각 테스트의 마지막 호출로 둔다.</remarks>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class UserAuthSpTests(DbFixture fixture)
{
    private const string Admin = "it.ua.admin";
    private const string Target = "it.ua.user";
    private const string Partner = "it.ua.ptn";
    private const string PartnerCompany = "ITUAPTN";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Future(int days = 30) => DateTime.Today.AddDays(days).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>본사 대상(테스트 역할: 공통코드 조회), 협력사 대상, 포털 관리자(현재 사용자)를 만든다.</summary>
    private static async Task<TestDb> BeginAsync(DbFixture fixture)
    {
        var db = await fixture.BeginAsync();
        await db.ExecAsync("""
            INSERT INTO FW.TB_ROLE (ROLE_ID, ROLE_NM, COMPANY_TYPE_CD, SORT_NO) VALUES ('IT_UA_HQ', N'본사 테스트', 'HQ', 900);
            INSERT INTO FW.TB_ROLE_PROGRAM_FUNC (ROLE_ID, PROGRAM_ID, FUNC_CD) VALUES ('IT_UA_HQ', 'SYS_CODE_MGT', 'READ');
            """);
        await db.SeedCompanyAsync(PartnerCompany);
        await db.SeedUserAsync(Admin, "HQ", "EMP", null, "PORTAL_ADMIN");
        await db.SeedUserAsync(Target, "HQ", "EMP", null, "IT_UA_HQ");
        await db.SeedUserAsync(Partner, PartnerCompany, "PARTNER", null, "PARTNER_USER");
        db.AsUser(Admin, "HQ", "EMP");
        return db;
    }

    private static Task<(UserAuthTarget? Target, IReadOnlyList<UserAuthRow> Rows)> GetAsync(TestDb db, string userId) =>
        db.Sp.QueryMultipleAsync(
            UserAuthService.ListSp,
            new { P_TARGET_USER_ID = userId },
            async r => (await r.ReadSingleAsync<UserAuthTarget>(Ct), await r.ReadAsync<UserAuthRow>(Ct)),
            Ct);

    private static async Task<byte[]> VersionAsync(TestDb db, string userId) =>
        await db.ScalarAsync<byte[]>("SELECT VERSION FROM FW.UFN_USER_AUTH_VERSION(@U)", ("@U", userId));

    /// <summary>"프로그램:기능:유형:종료일" 목록을 저장한다.</summary>
    private static async Task<int> SaveAsync(TestDb db, string userId, params string[] rows) =>
        (await db.Sp.ExecuteAsync(UserAuthService.SaveSp, new
        {
            P_TARGET_USER_ID = userId,
            P_VERSION = await VersionAsync(db, userId),
            P_ROWS = JsonSerializer.Serialize(rows.Select(r => r.Split(':')).Select(p => new { programId = p[0], funcCd = p[1], grantType = p[2], endDt = p[3] })),
        }, Ct)).AffectedCnt;

    private static Task<List<string>> ExceptionsAsync(TestDb db, string userId) =>
        db.ColumnAsync<string>(
            "SELECT PROGRAM_ID + ':' + FUNC_CD + ':' + RTRIM(GRANT_TYPE) + ':' + CONVERT(CHAR(10), END_DT, 23) FROM FW.TB_USER_PROGRAM_FUNC WHERE USER_ID = @U ORDER BY PROGRAM_ID, FUNC_CD",
            ("@U", userId));

    private static Task<long> AuthVersionAsync(TestDb db) => db.ScalarAsync<long>("SELECT VERSION_NO FROM FW.TB_CACHE_VERSION WHERE CACHE_KEY = 'AUTH'");

    [Fact]
    public async Task 조회_대상정보와_기능별역할허용_예외()
    {
        await using var db = await BeginAsync(fixture);
        await db.ExecAsync("INSERT INTO FW.TB_USER_PROGRAM_FUNC (USER_ID, PROGRAM_ID, FUNC_CD, GRANT_TYPE, END_DT) VALUES (@U, 'SYS_CODE_MGT', 'EXPORT', 'GRANT', '2099-12-31')", ("@U", Target));

        var (target, rows) = await GetAsync(db, Target);

        Assert.Equal(("HQ", "N", "N", 32), (target!.CompanyTypeCd, target.SysAdminYn, target.SelfYn, target.Version.Length));
        var code = rows.Where(r => r.ProgramId == "SYS_CODE_MGT").ToDictionary(r => r.FuncCd);
        Assert.Equal(("Y", null), (code["READ"].RoleYn, code["READ"].GrantType));
        Assert.Equal(("N", "GRANT", new DateTime(2099, 12, 31)), (code["EXPORT"].RoleYn, code["EXPORT"].GrantType?.Trim(), code["EXPORT"].EndDt));
        Assert.Equal(("Y", "N"), (rows.First(r => r.ProgramId == "SYS_USER_MGT").GrantableYn, rows.First(r => r.ProgramId == "PTN_USER_MGT").GrantableYn));
        Assert.DoesNotContain(rows, r => r.ProgramId == "SYS_CODE_MGT" && r.FuncCd == "APPROVE");
    }

    [Fact]
    public async Task 저장_추가변경삭제_감사로그_캐시_버전_같은상태면0()
    {
        await using var db = await BeginAsync(fixture);
        await db.ExecAsync("""
            INSERT INTO FW.TB_USER_PROGRAM_FUNC (USER_ID, PROGRAM_ID, FUNC_CD, GRANT_TYPE, END_DT)
            VALUES (@U, 'SYS_CODE_MGT', 'EXPORT', 'GRANT', '2099-12-31'), (@U, 'SYS_CODE_MGT', 'UPDATE', 'GRANT', '2099-12-31')
            """, ("@U", Target));
        var before = await AuthVersionAsync(db);
        var oldVersion = await VersionAsync(db, Target);
        var end = Future();

        var changed = await SaveAsync(db, Target, $"SYS_CODE_MGT:EXPORT:GRANT:{end}", $"SYS_CODE_MGT:READ:DENY:{end}");
        var again = await SaveAsync(db, Target, $"SYS_CODE_MGT:EXPORT:GRANT:{end}", $"SYS_CODE_MGT:READ:DENY:{end}");

        Assert.Equal((3, 0), (changed, again)); // EXPORT 종료일 변경, UPDATE 삭제, READ 차단 추가
        Assert.Equal([$"SYS_CODE_MGT:EXPORT:GRANT:{end}", $"SYS_CODE_MGT:READ:DENY:{end}"], await ExceptionsAsync(db, Target));
        Assert.Equal(before + 1, await AuthVersionAsync(db));
        Assert.NotEqual(oldVersion, await VersionAsync(db, Target));
        var audit = await db.ScalarAsync<string>("SELECT TOP (1) CONCAT(BEFORE_JSON, '|', AFTER_JSON) FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_USER_PROGRAM_FUNC' AND ROW_KEY = @U ORDER BY LOG_ID DESC", ("@U", Target));
        Assert.Equal($"""["SYS_CODE_MGT:EXPORT:GRANT:2099-12-31","SYS_CODE_MGT:UPDATE:GRANT:2099-12-31"]|["SYS_CODE_MGT:EXPORT:GRANT:{end}","SYS_CODE_MGT:READ:DENY:{end}"]""", audit);
    }

    [Fact]
    public async Task 저장_모두빼면빈목록_사용안함프로그램예외는그대로()
    {
        await using var db = await BeginAsync(fixture);
        await db.ExecAsync("""
            INSERT INTO FW.TB_USER_PROGRAM_FUNC (USER_ID, PROGRAM_ID, FUNC_CD, GRANT_TYPE) VALUES (@U, 'SYS_JOB_MGT', 'READ', 'GRANT'), (@U, 'SYS_CODE_MGT', 'READ', 'DENY');
            UPDATE FW.TB_PROGRAM SET USE_YN = 'N' WHERE PROGRAM_ID = 'SYS_JOB_MGT';
            """, ("@U", Target));

        Assert.Equal(1, await SaveAsync(db, Target));

        Assert.Equal(["SYS_JOB_MGT:READ:GRANT:9999-12-31"], await ExceptionsAsync(db, Target));
    }

    [Fact]
    public async Task 종료일_그대로둔지난예외는두고_새로넣는지난날짜는거부()
    {
        await using var db = await BeginAsync(fixture);
        var past = Future(-1);
        await db.ExecAsync("INSERT INTO FW.TB_USER_PROGRAM_FUNC (USER_ID, PROGRAM_ID, FUNC_CD, GRANT_TYPE, END_DT) VALUES (@U, 'SYS_CODE_MGT', 'EXPORT', 'GRANT', @D)", ("@U", Target), ("@D", past));
        Assert.Equal(1, await SaveAsync(db, Target, $"SYS_CODE_MGT:EXPORT:GRANT:{past}", $"SYS_CODE_MGT:READ:DENY:{Future(0)}"));

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, Target, $"SYS_CODE_MGT:EXPORT:DENY:{past}"));

        Assert.Equal(UserAuthMessageKeys.EndPast, ex.MessageKey);
    }

    [Fact]
    public async Task 협력사대상_열리지않은화면은추가허용못하고_차단은된다()
    {
        await using var db = await BeginAsync(fixture);
        Assert.Equal(1, await SaveAsync(db, Partner, $"SYS_USER_MGT:READ:DENY:{Future()}"));

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, Partner, $"SYS_USER_MGT:READ:GRANT:{Future()}"));

        Assert.Equal(UserAuthMessageKeys.Scope, ex.MessageKey);
    }

    [Fact]
    public async Task 데이터범위밖_대상은조회되지않고_저장은없는것으로본다()
    {
        await using var db = await BeginAsync(fixture);
        await db.ExecAsync("""
            INSERT INTO FW.TB_ROLE (ROLE_ID, ROLE_NM, COMPANY_TYPE_CD, DATA_SCOPE_CD, SORT_NO) VALUES ('IT_UA_SELF', N'본인 범위', 'HQ', 'SELF', 901);
            INSERT INTO FW.TB_ROLE_PROGRAM_FUNC (ROLE_ID, PROGRAM_ID, FUNC_CD) VALUES ('IT_UA_SELF', 'SYS_USER_AUTH_MGT', 'READ'), ('IT_UA_SELF', 'SYS_USER_AUTH_MGT', 'UPDATE');
            """);
        await db.SeedUserAsync("it.ua.narrow", "HQ", "EMP", null, "IT_UA_SELF");
        db.AsUser("it.ua.narrow", "HQ", "EMP");

        var (target, rows) = await GetAsync(db, Target);
        Assert.Equal((null, 0), (target, rows.Count));

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync(UserAuthService.SaveSp, new { P_TARGET_USER_ID = Target, P_VERSION = new byte[32], P_ROWS = "[]" }, Ct));

        Assert.Equal(MessageKeys.NotFound, ex.MessageKey);
    }

    [Fact]
    public async Task 시스템관리자대상은_예외권한을둘수없다()
    {
        await using var db = await BeginAsync(fixture);
        await db.SeedUserAsync("it.ua.root", "HQ", "EMP", null, "SYS_ADMIN");
        Assert.Equal("Y", (await GetAsync(db, "it.ua.root")).Target!.SysAdminYn);

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, "it.ua.root", $"SYS_CODE_MGT:READ:DENY:{Future()}"));

        Assert.Equal(UserAuthMessageKeys.SysAdmin, ex.MessageKey);
    }

    [Fact]
    public async Task 자기예외권한은_못고친다()
    {
        await using var db = await BeginAsync(fixture);
        Assert.Equal("Y", (await GetAsync(db, Admin)).Target!.SelfYn);

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, Admin, $"SYS_CODE_MGT:EXPORT:GRANT:{Future()}"));

        Assert.Equal(UserAuthMessageKeys.Self, ex.MessageKey);
    }

    [Theory]
    [InlineData("""[{"programId":"SYS_CODE_MGT","funcCd":"APPROVE","grantType":"GRANT","endDt":"2099-12-31"}]""")] // 프로그램이 쓰지 않는 기능
    [InlineData("""[{"programId":"SYS_CODE_MGT","funcCd":"READ","grantType":"ALLOW","endDt":"2099-12-31"}]""")] // 없는 유형
    [InlineData("""[{"programId":"SYS_CODE_MGT","funcCd":"READ","grantType":"DENY","endDt":"2099-12-31"},{"programId":"SYS_CODE_MGT","funcCd":"READ","grantType":"GRANT","endDt":"2099-12-31"}]""")] // 같은 기능 두 번
    [InlineData("""[{"programId":"SYS_CODE_MGT","funcCd":"READ","grantType":"DENY"}]""")] // 종료일 없음
    [InlineData("""{"programId":"SYS_CODE_MGT"}""")] // 배열이 아님
    public async Task 잘못된입력은_거부(string rows)
    {
        await using var db = await BeginAsync(fixture);
        var version = await VersionAsync(db, Target);

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync(UserAuthService.SaveSp, new { P_TARGET_USER_ID = Target, P_VERSION = version, P_ROWS = rows }, Ct));

        Assert.Equal(MessageKeys.InvalidInput, ex.MessageKey);
    }

    [Fact]
    public async Task 다른사람이먼저저장하면_동시성오류()
    {
        await using var db = await BeginAsync(fixture);
        var stale = await VersionAsync(db, Target);
        await SaveAsync(db, Target, $"SYS_CODE_MGT:READ:DENY:{Future()}");

        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync(UserAuthService.SaveSp, new { P_TARGET_USER_ID = Target, P_VERSION = stale, P_ROWS = "[]" }, Ct));

        Assert.Equal(MessageKeys.Concurrency, ex.MessageKey);
    }

    [Fact]
    public async Task 저장한예외가_권한계산에반영된다()
    {
        await using var db = await BeginAsync(fixture);
        await SaveAsync(db, Target, $"SYS_CODE_MGT:READ:DENY:{Future()}", $"SYS_USER_MGT:READ:GRANT:{Future()}");

        db.AsUser(Target, "HQ", "EMP");

        var funcs = await db.Sp.QueryMultipleAsync(
            "FW.USP_AUTH_GET_PERMISSION",
            null,
            async r =>
            {
                await r.ReadAsync<Portal.Security.Authorization.RoleScope>(Ct);
                await r.ReadAsync<Portal.Security.Authorization.ProgramFunc>(Ct);
                return await r.ReadAsync<Portal.Security.Authorization.UserProgramFunc>(Ct);
            },
            Ct);

        Assert.Equal(["SYS_CODE_MGT:READ:DENY", "SYS_USER_MGT:READ:GRANT"], funcs.Select(f => $"{f.ProgramId}:{f.FuncCd}:{f.GrantType.Trim()}").Order());
    }
}
