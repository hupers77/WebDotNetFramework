using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portal.Core.Config;
using Portal.Core.Context;
using Portal.Data;
using Portal.Infrastructure.Localization;
using Portal.Infrastructure.Messaging;
using Portal.Testing;
using Portal.Worker;

namespace Portal.UnitTests.Worker;

public sealed class WorkerTests
{
    [Fact]
    public async Task 서비스등록_시스템사용자로_메일발송기와작업이만들어진다()
    {
        // 개발 환경으로 만들면 등록 누락·수명 오류를 Build에서 검사한다
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Development, Args = [] });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Portal"] = "Server=unused;Database=unused" });
        builder.Services.AddPortalWorker(builder.Configuration);
        using var host = builder.Build();
        var provider = host.Services;
        await using var scope = provider.CreateAsyncScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<MailQueueSender>());
        Assert.Contains(provider.GetServices<IHostedService>(), s => s is MailSendService);
        var user = provider.GetRequiredService<ICurrentUser>();
        Assert.Equal(("SYSTEM", "SYSTEM"), (user.UserId, user.UserTypeCd));
    }

    [Fact]
    public async Task 한간격_가득찬묶음이면_이어보내고_최대횟수에서멈춘다()
    {
        var sp = new FakeSpExecutor();
        var full = Enumerable.Range(1, MailQueueSender.BatchSize).Select(i => new MailQueueSender.QueuedMail { MsgId = i, LangCd = "ko" }).ToList();
        sp.Returns(MailQueueSender.ClaimSp, full);
        var service = NewService(sp, new FakePortalConfig().Set("MAIL.ENABLED", "Y"));

        await service.RunTickAsync(CancellationToken.None);

        Assert.Equal(MailSendService.MaxBatchesPerTick, sp.Calls.Count(c => c.SpName == MailQueueSender.ClaimSp));
    }

    [Fact]
    public async Task 꺼져있으면_한번만확인_오류는삼키고다음간격에()
    {
        var off = new FakeSpExecutor();
        await NewService(off, new FakePortalConfig()).RunTickAsync(CancellationToken.None);
        Assert.DoesNotContain(off.Calls, c => c.SpName == MailQueueSender.ClaimSp);

        var broken = new FakeSpExecutor().Throws(MailQueueSender.ClaimSp, new InvalidOperationException("DB 연결 실패"));
        await NewService(broken, new FakePortalConfig().Set("MAIL.ENABLED", "Y")).RunTickAsync(CancellationToken.None);
        Assert.Single(broken.Calls, c => c.SpName == MailQueueSender.ClaimSp);
    }

    private static MailSendService NewService(FakeSpExecutor sp, FakePortalConfig config)
    {
        sp.Returns(LangResources.ListSp, new List<LangResources.ResourceRow>());
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<ISpExecutor>(sp);
        services.AddSingleton<IPortalConfig>(config);
        services.AddSingleton<LangResources>();
        services.AddSingleton<IMailTransport, NoTransport>();
        services.AddSingleton<Portal.Core.Security.IPersonalDataProtector, NoProtector>();
        services.TryAddScoped<MailQueueSender>();
        var provider = services.BuildServiceProvider();
        return new MailSendService(provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(new WorkerOptions()), NullLogger<MailSendService>.Instance);
    }

    private sealed class NoTransport : IMailTransport
    {
        public Task SendAsync(MailMessageData message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoProtector : Portal.Core.Security.IPersonalDataProtector
    {
        public byte[] Protect(string plainText) => [];

        public string Unprotect(byte[] protectedData) => "";
    }
}
