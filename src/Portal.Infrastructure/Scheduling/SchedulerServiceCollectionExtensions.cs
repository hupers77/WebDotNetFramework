using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portal.Core.Scheduling;
using Portal.Infrastructure.Files;
using Portal.Infrastructure.Logging;
using Portal.Infrastructure.Messaging;

namespace Portal.Infrastructure.Scheduling;

/// <summary>스케줄러 서비스 등록 (Portal.Worker에서만 부른다).</summary>
public static class SchedulerServiceCollectionExtensions
{
    /// <summary>
    /// 작업 실행기와 프레임워크 기본 작업(<c>MailSend</c>, <c>FileCleanup</c>, <c>LogIntegrity</c>)을 등록한다. <see cref="InfrastructureServiceCollectionExtensions.AddPortalInfrastructure"/> 다음에 부른다.
    /// 업무 모듈의 작업은 <c>services.AddScoped&lt;IScheduledJob, 내작업&gt;()</c>으로 추가한다.
    /// </summary>
    public static IServiceCollection AddPortalScheduler(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SchedulerOptions>().Bind(configuration.GetSection(SchedulerOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<JobScheduler>();
        services.AddScoped<IScheduledJob, MailSendJob>();
        services.AddScoped<IScheduledJob, FileCleanupJob>();
        services.AddScoped<IScheduledJob, LogIntegrityJob>();
        return services;
    }
}
