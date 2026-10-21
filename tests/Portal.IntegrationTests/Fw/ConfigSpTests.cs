using Portal.IntegrationTests.Infrastructure;

namespace Portal.IntegrationTests.Fw;

/// <summary>설정 목록 SP (설정 캐시 원천).</summary>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class ConfigSpTests(DbFixture fixture)
{
    public sealed class ConfigRow
    {
        public string ConfigKey { get; set; } = "";

        public string? ConfigVal { get; set; }

        public string ValTypeCd { get; set; } = "";

        public string EncYn { get; set; } = "";
    }

    [Fact]
    public async Task 전체설정_초기값()
    {
        await using var db = await fixture.BeginAsync();

        var rows = await db.Sp.QueryAsync<ConfigRow>("FW.USP_CONFIG_GET_LIST", null, TestContext.Current.CancellationToken);

        var lockCount = Assert.Single(rows, r => r.ConfigKey == "PWD.LOCK_COUNT");
        Assert.Equal("5", lockCount.ConfigVal);
        Assert.Equal("INT", lockCount.ValTypeCd);
        Assert.Equal(await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_CONFIG"), rows.Count);
    }
}
