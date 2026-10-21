using Portal.IntegrationTests.Infrastructure;

namespace Portal.IntegrationTests.Fw;

/// <summary>다국어 문구 목록 SP (다국어 캐시 원천).</summary>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class LangResourceSpTests(DbFixture fixture)
{
    public sealed class ResourceRow
    {
        public string ResKey { get; set; } = "";

        public string LangCd { get; set; } = "";

        public string ResText { get; set; } = "";
    }

    [Fact]
    public async Task 사용언어문구전체_3개언어()
    {
        await using var db = await fixture.BeginAsync();

        var rows = await db.Sp.QueryAsync<ResourceRow>("FW.USP_LANG_RESOURCE_GET_LIST", null, TestContext.Current.CancellationToken);

        Assert.Equal(["en", "ko", "zh-CN"], rows.Select(r => r.LangCd).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal("로그인이 필요합니다.", Assert.Single(rows, r => r.ResKey == "MSG.LOGIN_REQUIRED" && r.LangCd == "ko").ResText);
        Assert.Equal(await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_LANG_RESOURCE"), rows.Count);
    }

    [Fact]
    public async Task 사용중지언어_제외()
    {
        await using var db = await fixture.BeginAsync();
        await db.ExecAsync("UPDATE FW.TB_LANG SET USE_YN = 'N' WHERE LANG_CD = 'zh-CN'");

        var rows = await db.Sp.QueryAsync<ResourceRow>("FW.USP_LANG_RESOURCE_GET_LIST", null, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(rows, r => r.LangCd == "zh-CN");
    }
}
