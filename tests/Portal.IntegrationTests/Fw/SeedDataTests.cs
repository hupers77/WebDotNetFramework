using System.Reflection;
using Portal.Core.Keys;
using Portal.IntegrationTests.Infrastructure;
using Portal.Security.Authentication;
using Portal.Security.Passwords;

namespace Portal.IntegrationTests.Fw;

/// <summary>초기 데이터가 초기 데이터 정의서·코드 상수와 맞는지 확인한다. 시드나 상수를 바꾸면 여기서 어긋남이 드러난다.</summary>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class SeedDataTests(DbFixture fixture)
{
    private static readonly string[] Languages = ["ko", "en", "zh-CN"];

    [Fact]
    public async Task 설정키_ConfigKeys상수와_TB_CONFIG가같음()
    {
        await using var db = await fixture.BeginAsync();
        var constants = typeof(ConfigKeys).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal);

        var rows = await db.ColumnAsync<string>("SELECT CONFIG_KEY FROM FW.TB_CONFIG ORDER BY CONFIG_KEY");

        Assert.Equal(constants, rows.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task 기능코드_FuncCodes상수가모두공통코드에있음()
    {
        await using var db = await fixture.BeginAsync();
        var constants = typeof(FuncCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!);

        var rows = await db.ColumnAsync<string>("SELECT CODE FROM FW.TB_CODE WHERE GROUP_CD = 'FUNC_CD'");

        Assert.All(constants, c => Assert.Contains(c, rows));
    }

    [Theory]
    [InlineData(typeof(MessageKeys))]
    [InlineData(typeof(AuthMessageKeys))]
    [InlineData(typeof(PasswordMessageKeys))]
    [InlineData(typeof(PartnerJoinMessageKeys))]
    [InlineData(typeof(Portal.Web.Areas.Sys.Models.JobMessageKeys))]
    public async Task 메시지키_상수가모두3개언어로있음(Type keys)
    {
        await using var db = await fixture.BeginAsync();
        var constants = keys.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!);

        foreach (var key in constants)
        {
            var langs = await db.ColumnAsync<string>("SELECT LANG_CD FROM FW.TB_LANG_RESOURCE WHERE RES_KEY = @K", ("@K", key));
            Assert.True(Languages.All(langs.Contains), $"{key}: 3개 언어 문구가 모두 있어야 합니다 (있음: {string.Join(",", langs)})");
        }
    }

    [Fact]
    public async Task 캐시키_CacheKeys상수와같음()
    {
        await using var db = await fixture.BeginAsync();
        var constants = typeof(CacheKeys).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal);

        Assert.Equal(constants, await db.ColumnAsync<string>("SELECT CACHE_KEY FROM FW.TB_CACHE_VERSION ORDER BY CACHE_KEY"));
    }

    [Fact]
    public async Task 로그인결과코드_LoginResultCodes상수가모두공통코드에있음()
    {
        await using var db = await fixture.BeginAsync();
        var constants = typeof(LoginResultCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!);

        var rows = await db.ColumnAsync<string>("SELECT CODE FROM FW.TB_CODE WHERE GROUP_CD = 'LOGIN_RESULT'");

        Assert.All(constants, c => Assert.Contains(c, rows));
    }

    [Theory]
    [InlineData("SELECT COUNT(*) FROM FW.TB_ROLE", 7)]
    [InlineData("SELECT COUNT(*) FROM FW.TB_PROGRAM", 34)]
    [InlineData("SELECT COUNT(*) FROM FW.TB_LANG", 3)]
    // 문구·명칭이 빠진 행: 0이어야 한다
    [InlineData("SELECT COUNT(*) FROM FW.TB_MENU M CROSS JOIN FW.TB_LANG L WHERE NOT EXISTS (SELECT 1 FROM FW.TB_MENU_LANG ML WHERE ML.MENU_ID = M.MENU_ID AND ML.LANG_CD = L.LANG_CD)", 0)]
    [InlineData("SELECT COUNT(*) FROM FW.TB_CODE C CROSS JOIN FW.TB_LANG L WHERE NOT EXISTS (SELECT 1 FROM FW.TB_CODE_LANG CL WHERE CL.GROUP_CD = C.GROUP_CD AND CL.CODE = C.CODE AND CL.LANG_CD = L.LANG_CD)", 0)]
    [InlineData("SELECT COUNT(*) FROM FW.TB_MENU WHERE MENU_TYPE_CD = 'PAGE' AND PROGRAM_ID IS NULL", 0)]
    [InlineData("SELECT COUNT(*) FROM FW.TB_PROGRAM P WHERE NOT EXISTS (SELECT 1 FROM FW.TB_MENU M WHERE M.PROGRAM_ID = P.PROGRAM_ID)", 0)]
    [InlineData("SELECT COUNT(*) FROM FW.TB_PROGRAM P WHERE NOT EXISTS (SELECT 1 FROM FW.TB_PROGRAM_FUNC F WHERE F.PROGRAM_ID = P.PROGRAM_ID AND F.FUNC_CD = 'READ')", 0)]
    // 기능코드는 공통코드 FUNC_CD에 있어야 한다
    [InlineData("SELECT COUNT(*) FROM FW.TB_PROGRAM_FUNC F WHERE NOT EXISTS (SELECT 1 FROM FW.TB_CODE C WHERE C.GROUP_CD = 'FUNC_CD' AND C.CODE = F.FUNC_CD)", 0)]
    // 감사자는 관리·로그 화면에서 조회·내보내기만 한다
    [InlineData("SELECT COUNT(*) FROM FW.TB_ROLE_PROGRAM_FUNC WHERE ROLE_ID = 'SECURITY_AUDITOR' AND FUNC_CD NOT IN ('READ', 'EXPORT') AND (PROGRAM_ID LIKE 'SYS[_]%' OR PROGRAM_ID LIKE 'LOG[_]%')", 0)]
    // 협력사 역할은 본사 전용(EMP) 메뉴의 프로그램 권한을 갖지 않는다
    [InlineData("SELECT COUNT(*) FROM FW.TB_ROLE_PROGRAM_FUNC F JOIN FW.TB_MENU M ON M.PROGRAM_ID = F.PROGRAM_ID WHERE F.ROLE_ID LIKE 'PARTNER%' AND M.OPEN_SCOPE_CD = 'EMP'", 0)]
    public async Task 초기데이터_정합성(string sql, int expected)
    {
        await using var db = await fixture.BeginAsync();

        Assert.Equal(expected, await db.ScalarAsync<int>(sql));
    }
}
