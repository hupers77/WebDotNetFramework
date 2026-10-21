using Microsoft.Data.SqlClient;
using Portal.IntegrationTests.Infrastructure;

namespace Portal.IntegrationTests.Fw;

/// <summary>애플리케이션 DB 역할(portal_app) 권한: SP 실행만 허용, 테이블 직접 접근과 로그 변경은 차단(설계서 8·9장).</summary>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class DbSecurityTests(DbFixture fixture)
{
    [Fact]
    public async Task 앱역할_SP실행가능()
    {
        await using var db = await fixture.BeginAsync();
        await CreateAppUserAsync(db);

        var count = await db.ScalarAsync<int>(
            "EXECUTE AS USER = 'it_app'; " +
            "DECLARE @T TABLE (CACHE_KEY VARCHAR(50), VERSION_NO BIGINT); " +
            "INSERT INTO @T EXEC FW.USP_CACHE_VERSION_GET_LIST @P_USER_ID = 'x', @P_LANG_CD = 'ko'; " +
            "REVERT; SELECT COUNT(*) FROM @T;");

        Assert.Equal(5, count);
    }

    [Theory]
    [InlineData("SELECT TOP (1) USER_ID FROM FW.TB_USER")]
    [InlineData("UPDATE FW.TB_CONFIG SET CONFIG_VAL = '1' WHERE 1 = 0")]
    [InlineData("DELETE FROM LOG.TB_AUDIT_LOG WHERE 1 = 0")]
    [InlineData("UPDATE LOG.TB_LOGIN_LOG SET USER_ID = 'x' WHERE 1 = 0")]
    [InlineData("DELETE FROM LOG.TB_ACCESS_LOG WHERE 1 = 0")]
    public async Task 앱역할_테이블직접접근_거부(string sql)
    {
        await using var db = await fixture.BeginAsync();
        await CreateAppUserAsync(db);

        var ex = await Assert.ThrowsAsync<SqlException>(() => db.ExecAsync($"EXECUTE AS USER = 'it_app'; {sql}; REVERT;"));

        Assert.Equal(229, ex.Number); // permission denied
    }

    private static async Task CreateAppUserAsync(TestDb db)
    {
        await db.ExecAsync("CREATE USER [it_app] WITHOUT LOGIN");
        await db.ExecAsync("ALTER ROLE [portal_app] ADD MEMBER [it_app]");
    }
}
