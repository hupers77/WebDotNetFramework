using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portal.Core.Config;
using Portal.Data;
using Portal.Infrastructure.Caching;
using Portal.Infrastructure.Config;
using Portal.Testing;

namespace Portal.UnitTests.Infrastructure;

public sealed class PortalConfigTests
{
    private readonly FakeSpExecutor _sp = new();
    private readonly ServiceProvider _services;

    public PortalConfigTests()
    {
        _services = new ServiceCollection().AddSingleton<ISpExecutor>(_sp).BuildServiceProvider();
        SetConfig(("PWD.LOCK_COUNT", "5", "N"), ("AUTH.PARTNER_MFA", "Y", "N"), ("SITE.NAME", "", "N"), ("MAIL.PASSWORD", "xx", "Y"));
        SetVersions(("CONFIG", 1));
    }

    private void SetConfig(params (string Key, string? Val, string Enc)[] rows) =>
        _sp.Returns(PortalConfig.ListSp, rows.Select(r => new PortalConfig.ConfigRow { ConfigKey = r.Key, ConfigVal = r.Val, EncYn = r.Enc }).ToList());

    private void SetVersions(params (string Key, long Version)[] rows) =>
        _sp.Returns(CacheVersionMonitor.VersionSp, rows.Select(r => new CacheVersionMonitor.VersionRow { CacheKey = r.Key, VersionNo = r.Version }).ToList());

    private async Task<PortalConfig> LoadedAsync()
    {
        var config = new PortalConfig(_services.GetRequiredService<IServiceScopeFactory>());
        await config.ReloadAsync(TestContext.Current.CancellationToken);
        return config;
    }

    private CacheVersionMonitor Monitor(params ICacheVersionListener[] listeners) =>
        new(_services.GetRequiredService<IServiceScopeFactory>(), listeners, Options.Create(new CacheOptions()), new FakeTimeProvider(), NullLogger<CacheVersionMonitor>.Instance);

    [Fact]
    public async Task 형식별읽기()
    {
        var config = await LoadedAsync();

        Assert.Equal(5, config.GetInt("PWD.LOCK_COUNT"));
        Assert.True(config.GetBool("AUTH.PARTNER_MFA"));
        Assert.Null(config.GetString("SITE.NAME"));
        Assert.Null(config.GetString("NO.SUCH_KEY"));
    }

    [Fact]
    public async Task 없는키나형식오류_숨기지않고예외()
    {
        var config = await LoadedAsync();

        Assert.Equal("NO.SUCH_KEY", Assert.Throws<ConfigValueException>(() => config.GetInt("NO.SUCH_KEY")).Key);
        Assert.Throws<ConfigValueException>(() => config.GetBool("PWD.LOCK_COUNT"));
    }

    [Fact]
    public async Task 암호화값_일반읽기금지()
    {
        var config = await LoadedAsync();

        Assert.Throws<ConfigValueException>(() => config.GetString("MAIL.PASSWORD"));
    }

    [Fact]
    public void 읽기전_예외() =>
        Assert.Throws<InvalidOperationException>(() => new PortalConfig(_services.GetRequiredService<IServiceScopeFactory>()).GetString("X"));

    [Fact]
    public async Task 캐시버전이바뀌면_다시읽는다_안바뀌면그대로()
    {
        var config = new PortalConfig(_services.GetRequiredService<IServiceScopeFactory>());
        var monitor = Monitor(config);
        var ct = TestContext.Current.CancellationToken;
        await monitor.StartAsync(ct);
        await monitor.StopAsync(ct);

        SetConfig(("PWD.LOCK_COUNT", "3", "N"));
        await monitor.CheckOnceAsync(ct);
        Assert.Equal(5, config.GetInt("PWD.LOCK_COUNT"));

        SetVersions(("CONFIG", 2));
        await monitor.CheckOnceAsync(ct);
        Assert.Equal(3, config.GetInt("PWD.LOCK_COUNT"));
    }

    [Fact]
    public async Task 다시읽기실패_다음주기에재시도()
    {
        var listener = new FlakyListener();
        var monitor = Monitor(listener);
        var ct = TestContext.Current.CancellationToken;
        await monitor.StartAsync(ct);
        await monitor.StopAsync(ct);

        SetVersions(("CONFIG", 2));
        listener.FailNext = true;
        await monitor.CheckOnceAsync(ct);
        await monitor.CheckOnceAsync(ct);
        await monitor.CheckOnceAsync(ct);

        Assert.Equal(3, listener.Calls); // 시작 1 + 실패 1 + 재시도 성공 1, 이후는 변화 없음
    }

    private sealed class FlakyListener : ICacheVersionListener
    {
        public bool FailNext { get; set; }

        public int Calls { get; private set; }

        public string CacheKey => "CONFIG";

        public Task ReloadAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("DB 일시 오류");
            }

            return Task.CompletedTask;
        }
    }
}
