using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Portal.Core.Context;
using Portal.Data;
using Portal.Infrastructure;
using Portal.Security;

namespace Portal.Worker;

/// <summary>Portal.Worker 서비스 등록. 웹과 같은 DB·설정·Data Protection 키를 쓴다.</summary>
public static class WorkerServices
{
    /// <summary>데이터·보안·설정·메일 서비스와 배치 작업을 등록한다.</summary>
    public static IServiceCollection AddPortalWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ICurrentUser, WorkerCurrentUser>();
        services.AddPortalData(configuration);
        services.AddPortalSecurity();
        services.AddPortalDataProtection(configuration);
        services.AddPortalInfrastructure(configuration);
        services.AddOptions<WorkerOptions>().Bind(configuration.GetSection(WorkerOptions.SectionName));
        services.AddHostedService<MailSendService>();
        return services;
    }
}
