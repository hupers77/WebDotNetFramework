using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Data;
using Portal.IntegrationTests.Infrastructure;

namespace Portal.IntegrationTests.Fw;

/// <summary>설정 SP: 캐시 원천 목록, 관리 화면 목록(비밀값 숨김), 저장(이력·감사·캐시 버전), 변경 이력.</summary>
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

    public sealed class AdminRow
    {
        public string ConfigKey { get; set; } = "";

        public string? ConfigVal { get; set; }

        public string SecretSetYn { get; set; } = "";

        public string UpdId { get; set; } = "";

        public byte[] RowVer { get; set; } = [];
    }

    /// <summary>FW.TVP_CONFIG 컬럼 순서.</summary>
    public sealed class SaveRow
    {
        public string ConfigKey { get; set; } = "";

        public string? ConfigVal { get; set; }

        public byte[] RowVer { get; set; } = [];
    }

    public sealed class HistRow
    {
        public string? OldVal { get; set; }

        public string? NewVal { get; set; }

        public string ChgId { get; set; } = "";
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<AdminRow> GetAsync(TestDb db, string key) =>
        Assert.Single((await db.Sp.QueryPageAsync<AdminRow>("FW.USP_CONFIG_ADMIN_GET_LIST", new { P_KEYWORD = key, P_PAGE_NO = 1, P_PAGE_SIZE = 500 }, Ct)).Items, r => r.ConfigKey == key);

    private static async Task<int> SaveAsync(TestDb db, params SaveRow[] rows) =>
        (await db.Sp.ExecuteAsync("FW.USP_CONFIG_SAVE", new { P_ROWS = Tvp.From("FW.TVP_CONFIG", rows) }, Ct)).AffectedCnt;

    private static async Task<SaveRow> RowAsync(TestDb db, string key, string? value) =>
        new() { ConfigKey = key, ConfigVal = value, RowVer = (await GetAsync(db, key)).RowVer };

    [Fact]
    public async Task 관리목록_분류검색_비밀값은입력여부만()
    {
        await using var db = await fixture.BeginAsync();
        await db.ExecAsync("UPDATE FW.TB_CONFIG SET CONFIG_VAL = N'cipher' WHERE CONFIG_KEY = 'MAIL.SMTP_PASSWORD'");

        var mail = await db.Sp.QueryPageAsync<AdminRow>("FW.USP_CONFIG_ADMIN_GET_LIST", new { P_CATEGORY_CD = "MAIL", P_PAGE_NO = 1, P_PAGE_SIZE = 500 }, Ct);

        Assert.All(mail.Items, r => Assert.StartsWith("MAIL.", r.ConfigKey, StringComparison.Ordinal));
        Assert.Equal(await db.ScalarAsync<int>("SELECT COUNT(*) FROM FW.TB_CONFIG WHERE CATEGORY_CD = 'MAIL'"), mail.TotalCount);
        var secret = Assert.Single(mail.Items, r => r.ConfigKey == "MAIL.SMTP_PASSWORD");
        Assert.Null(secret.ConfigVal);
        Assert.Equal("Y", secret.SecretSetYn);
        Assert.Equal("N", (await GetAsync(db, "LDAP.BIND_PASSWORD")).SecretSetYn);
    }

    [Fact]
    public async Task 저장_바뀐값만반영_이력감사캐시버전()
    {
        await using var db = await fixture.BeginAsync();
        var cacheBefore = await db.ScalarAsync<long>("SELECT VERSION_NO FROM FW.TB_CACHE_VERSION WHERE CACHE_KEY = 'CONFIG'");
        var current = await GetAsync(db, "PWD.LOCK_COUNT");

        var saved = await SaveAsync(db, await RowAsync(db, "PWD.LOCK_COUNT", "6"), await RowAsync(db, "PWD.HISTORY_COUNT", (await GetAsync(db, "PWD.HISTORY_COUNT")).ConfigVal));

        Assert.Equal(1, saved);
        var after = await GetAsync(db, "PWD.LOCK_COUNT");
        Assert.Equal(("6", "it.admin"), (after.ConfigVal, after.UpdId));
        Assert.NotEqual(current.RowVer, after.RowVer);
        var hist = Assert.Single((await db.Sp.QueryPageAsync<HistRow>("FW.USP_CONFIG_HIST_GET_LIST", new { P_CONFIG_KEY = "PWD.LOCK_COUNT", P_PAGE_NO = 1, P_PAGE_SIZE = 100 }, Ct)).Items);
        Assert.Equal(("5", "6", "it.admin"), (hist.OldVal, hist.NewVal, hist.ChgId));
        Assert.Empty((await db.Sp.QueryPageAsync<HistRow>("FW.USP_CONFIG_HIST_GET_LIST", new { P_CONFIG_KEY = "PWD.HISTORY_COUNT", P_PAGE_NO = 1, P_PAGE_SIZE = 100 }, Ct)).Items);
        Assert.Equal(cacheBefore + 1, await db.ScalarAsync<long>("SELECT VERSION_NO FROM FW.TB_CACHE_VERSION WHERE CACHE_KEY = 'CONFIG'"));
        Assert.Contains("\"CONFIG_VAL\":\"6\"", await db.ScalarAsync<string>("SELECT TOP (1) AFTER_JSON FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_CONFIG' ORDER BY LOG_ID DESC"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 저장_값이같으면_0건_캐시버전그대로()
    {
        await using var db = await fixture.BeginAsync();
        var cacheBefore = await db.ScalarAsync<long>("SELECT VERSION_NO FROM FW.TB_CACHE_VERSION WHERE CACHE_KEY = 'CONFIG'");

        Assert.Equal(0, await SaveAsync(db, await RowAsync(db, "PWD.LOCK_COUNT", "5")));
        Assert.Equal(cacheBefore, await db.ScalarAsync<long>("SELECT VERSION_NO FROM FW.TB_CACHE_VERSION WHERE CACHE_KEY = 'CONFIG'"));
    }

    [Fact]
    public async Task 저장_비밀값은_이력감사에값을남기지않는다()
    {
        await using var db = await fixture.BeginAsync();

        await SaveAsync(db, await RowAsync(db, "MAIL.SMTP_PASSWORD", "cipher-text"));

        Assert.Equal("cipher-text", await db.ScalarAsync<string>("SELECT CONFIG_VAL FROM FW.TB_CONFIG WHERE CONFIG_KEY = 'MAIL.SMTP_PASSWORD'"));
        var hist = Assert.Single((await db.Sp.QueryPageAsync<HistRow>("FW.USP_CONFIG_HIST_GET_LIST", new { P_CONFIG_KEY = "MAIL.SMTP_PASSWORD", P_PAGE_NO = 1, P_PAGE_SIZE = 100 }, Ct)).Items);
        Assert.Equal((null, null), (hist.OldVal, hist.NewVal));
        var audit = await db.ScalarAsync<string>("SELECT TOP (1) AFTER_JSON FROM LOG.TB_AUDIT_LOG WHERE TABLE_NM = 'FW.TB_CONFIG' ORDER BY LOG_ID DESC");
        Assert.DoesNotContain("cipher-text", audit, StringComparison.Ordinal);
        Assert.Contains("***", audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 저장_다른사용자가먼저수정_CONCURRENCY()
    {
        await using var db = await fixture.BeginAsync();
        var stale = await RowAsync(db, "PWD.LOCK_COUNT", "7");
        await db.ExecAsync("UPDATE FW.TB_CONFIG SET CONFIG_VAL = N'6' WHERE CONFIG_KEY = 'PWD.LOCK_COUNT'");

        var ex = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, stale));

        Assert.Equal(MessageKeys.Concurrency, ex.MessageKey);
    }

    [Fact]
    public async Task 저장_없는키_NOT_FOUND_빈목록_NO_CHANGES()
    {
        await using var db = await fixture.BeginAsync();

        var notFound = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db, new SaveRow { ConfigKey = "NO.SUCH", ConfigVal = "a", RowVer = new byte[8] }));
        var empty = await Assert.ThrowsAsync<BizException>(() => SaveAsync(db));

        Assert.Equal(MessageKeys.NotFound, notFound.MessageKey);
        Assert.Equal(MessageKeys.NoChanges, empty.MessageKey);
    }
}
