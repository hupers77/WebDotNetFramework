using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Portal.Core.Config;
using Portal.Infrastructure.Caching;
using Portal.Infrastructure.Config;

namespace Portal.Infrastructure;

/// <summary>Portal.Infrastructure 서비스 등록.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>설정 캐시와 캐시 버전 감시를 등록한다.</summary>
    public static IServiceCollection AddPortalInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CacheOptions>().Bind(configuration.GetSection(CacheOptions.SectionName));
        services.AddSingleton<PortalConfig>();
        services.AddSingleton<IPortalConfig>(sp => sp.GetRequiredService<PortalConfig>());
        services.AddSingleton<ICacheVersionListener>(sp => sp.GetRequiredService<PortalConfig>());
        services.AddHostedService<CacheVersionMonitor>();
        return services;
    }
}
