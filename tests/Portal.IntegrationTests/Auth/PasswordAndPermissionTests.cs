using Portal.Core.Errors;
using Portal.IntegrationTests.Infrastructure;
using Portal.Security.Authentication;
using Portal.Security.Authorization;
using Portal.Security.Passwords;
using static Portal.IntegrationTests.Auth.AuthTestContext;

namespace Portal.IntegrationTests.Auth;

/// <summary>비밀번호 변경·이력, 초기 관리자, 권한 원천 데이터와 평가.</summary>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class PasswordAndPermissionTests(DbFixture fixture)
{
    [Fact]
    public async Task 비밀번호변경_이력은설정개수만_재사용금지_감사로그에해시없음()
    {
        await using var db = await fixture.BeginAsync();
        await SeedLoginUserAsync(db, "it.hong", "Start!2026a");
        db.AsUser("it.hong", "HQ", "EMP");
        var service = NewPassword(db);

        string[] passwords = ["Second!2026a", "Third!2026a", "Fourth!2026a", "Fifth!2026a"];
        var current = "Start!2026a";
        foreach (var next in passwords)
        {
            await service.ChangeAsync(current, next);
            current = next;
        }

        Assert.Equal(3, await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_USER_PWD_HIST WHERE USER_ID = 'it.hong'"));
        var reused = await Assert.ThrowsAsync<BizException>(() => service.ChangeAsync(current, "Third!2026a"));
        Assert.Equal(PasswordMessageKeys.Reused, reused.MessageKey);

        // 4번 전 비밀번호는 이력에서 빠져 다시 쓸 수 있다
        await service.ChangeAsync(current, "Start!2026a");
        Assert.Equal("N", await db.ScalarAsync<string>("SELECT PWD_CHG_REQ_YN FROM FW.TB_USER WHERE USER_ID = 'it.hong'"));
        Assert.Equal(0, await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM LOG.TB_AUDIT_LOG WHERE ROW_KEY = 'it.hong' AND TABLE_NM = 'FW.TB_USER' AND (AFTER_JSON LIKE '%HASH%' OR AFTER_JSON LIKE '%SALT%')"));
        Assert.Equal(5, await db.ScalarAsync<int>("SELECT COUNT(*) FROM LOG.TB_AUDIT_LOG WHERE ROW_KEY = 'it.hong' AND TABLE_NM = 'FW.TB_USER'"));
    }

    [Fact]
    public async Task 비밀번호이력SP_다른사용자해시는볼수없다()
    {
        await using var db = await fixture.BeginAsync();
        await SeedLoginUserAsync(db, "it.hong", "Start!2026a");
        await SeedLoginUserAsync(db, "it.kim", "Other!2026a");
        db.AsUser("it.kim", "HQ", "EMP");

        // 대상 사용자 파라미터가 없고 공통 파라미터 @P_USER_ID(서버가 주입)로만 조회한다
        var rows = await db.Sp.QueryAsync<HistRow>("FW.USP_AUTH_PWD_HIST_GET_LIST", null, TestContext.Current.CancellationToken);

        var row = Assert.Single(rows);
        Assert.True(Hasher.Verify("Other!2026a", row.PwdHash, row.PwdSalt));
    }

    [Fact]
    public async Task 초기관리자_생성후임시비밀번호로로그인_변경강제_재실행은재설정()
    {
        await using var db = await fixture.BeginAsync();
        await db.ExecAsync("DELETE FROM FW.TB_USER_ROLE WHERE USER_ID = 'sysadmin'; DELETE FROM FW.TB_USER_SESSION WHERE USER_ID = 'sysadmin'; DELETE FROM FW.TB_USER_PWD_HIST WHERE USER_ID = 'sysadmin'; DELETE FROM FW.TB_USER WHERE USER_ID = 'sysadmin'");
        AsAnonymous(db);
        var bootstrap = new AdminBootstrapService(db.Sp, Hasher);

        var (created, temp) = await bootstrap.InitAsync();
        var login = await NewAuth(db).LoginAsync(new LoginRequest("sysadmin", temp, null));
        var (createdAgain, temp2) = await bootstrap.InitAsync();

        Assert.True(created);
        Assert.True(login.Succeeded);
        Assert.True(login.PasswordChangeRequired);
        Assert.False(createdAgain);
        Assert.NotEqual(temp, temp2);
        Assert.Equal(LoginResultCodes.Disabled, (await NewAuth(db).CheckSessionAsync(login.SessionToken)).EndReasonCd);
        Assert.Equal(["SYS_ADMIN"], await db.ColumnAsync<string>("SELECT ROLE_ID FROM FW.UFN_USER_ROLES('sysadmin')"));
    }

    [Fact]
    public async Task 권한_역할합집합_사용자GRANT추가_DENY우선()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedUserAsync("it.hong", "HQ", "EMP", roles: ["EMP_USER"]);
        await db.ExecAsync("""
            INSERT INTO FW.TB_USER_PROGRAM_FUNC (USER_ID, PROGRAM_ID, FUNC_CD, GRANT_TYPE) VALUES
              ('it.hong', 'SYS_CODE_MGT', 'READ', 'GRANT'),
              ('it.hong', 'MY_PROFILE', 'READ', 'DENY'),
              ('it.hong', 'SYS_USER_MGT', 'READ', 'GRANT')
            UPDATE FW.TB_USER_PROGRAM_FUNC SET END_DT = DATEADD(DAY, -1, CAST(GETDATE() AS DATE)) WHERE USER_ID = 'it.hong' AND PROGRAM_ID = 'SYS_USER_MGT'
            """);
        db.AsUser("it.hong", "HQ", "EMP");

        var set = await NewPermission(db).GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["EMP_USER"], set.Roles);
        Assert.True(set.Can("MY_PWD_CHG", "READ"));      // 역할 권한
        Assert.True(set.Can("SYS_CODE_MGT", "READ"));    // GRANT
        Assert.False(set.Can("MY_PROFILE", "READ"));     // DENY가 역할 허용보다 우선
        Assert.False(set.Can("SYS_USER_MGT", "READ"));   // 기간 지난 예외는 무시
        Assert.False(set.IsSysAdmin);
    }

    [Fact]
    public async Task 권한_시스템관리자전체_협력사범위상한()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedCompanyAsync("ITP01");
        await db.SeedUserAsync("it.part", "ITP01", "PARTNER", roles: ["PORTAL_ADMIN"]);

        var admin = await NewPermission(db).GetCurrentAsync(TestContext.Current.CancellationToken);
        db.AsUser("it.part", "ITP01", "PARTNER");
        var partner = await NewPermission(db).GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.True(admin.IsSysAdmin);
        Assert.True(admin.Can("LOG_AUDIT_INQ", "READ"));
        Assert.Equal(DataScopes.All, admin.DataScopeCd);
        Assert.Equal(DataScopes.Company, partner.DataScopeCd);
    }

    [Fact]
    public async Task 권한_사용안함프로그램은제외()
    {
        await using var db = await fixture.BeginAsync();
        await db.SeedUserAsync("it.hong", "HQ", "EMP", roles: ["EMP_USER"]);
        await db.ExecAsync("UPDATE FW.TB_PROGRAM SET USE_YN = 'N' WHERE PROGRAM_ID = 'MY_PWD_CHG'");
        db.AsUser("it.hong", "HQ", "EMP");

        var set = await NewPermission(db).GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.False(set.Can("MY_PWD_CHG", "READ"));
    }

    public sealed class HistRow
    {
        public int SeqNo { get; set; }

        public byte[] PwdHash { get; set; } = [];

        public byte[] PwdSalt { get; set; } = [];
    }
}
