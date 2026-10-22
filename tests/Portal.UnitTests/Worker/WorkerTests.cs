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
using Portal.Core.Scheduling;
using Portal.Infrastructure.Messaging;
using Portal.Infrastructure.Scheduling;
using Portal.Testing;
using Portal.Worker;

namespace Portal.UnitTests.Worker;

public sealed class WorkerTests
{
    [Fact]
    public async Task 서비스등록_시스템사용자로_스케줄러와기본작업이만들어진다()
    {
        // 개발 환경으로 만들면 등록 누락·수명 오류를 Build에서 검사한다
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Development, Args = [] });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Portal"] = "Server=unused;Database=unused" });
        builder.Services.AddPortalWorker(builder.Configuration);
        using var host = builder.Build();
        var provider = host.Services;
        await using var scope = provider.CreateAsyncScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<MailQueueSender>());
        Assert.Equal(["MailSend", "FileCleanup"], scope.ServiceProvider.GetServices<IScheduledJob>().Select(j => j.Name));
        Assert.Contains(provider.GetServices<IHostedService>(), s => s is SchedulerService);
        Assert.Equal(TimeSpan.FromHours(9), provider.GetRequiredService<JobScheduler>().TimeZone.BaseUtcOffset);
        var user = provider.GetRequiredService<ICurrentUser>();
        Assert.Equal(("SYSTEM", "SYSTEM"), (user.UserId, user.UserTypeCd));
    }

    [Fact]
    public async Task 확인중오류는_삼키고다음간격에()
    {
        var broken = new FakeSpExecutor().Throws(JobScheduler.DueSp, new InvalidOperationException("DB 연결 실패"));
        var services = new ServiceCollection().AddSingleton<ISpExecutor>(broken).BuildServiceProvider();
        var options = Options.Create(new SchedulerOptions());
        var scheduler = new JobScheduler(services.GetRequiredService<IServiceScopeFactory>(), options, TimeProvider.System, NullLogger<JobScheduler>.Instance);
        var service = new SchedulerService(scheduler, options, NullLogger<SchedulerService>.Instance);

        await service.PollOnceAsync(CancellationToken.None);

        Assert.Single(broken.Calls, c => c.SpName == JobScheduler.DueSp);
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
