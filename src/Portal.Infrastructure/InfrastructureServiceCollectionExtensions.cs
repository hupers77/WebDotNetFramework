using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Portal.Core.Config;
using Portal.Core.Localization;
using Portal.Infrastructure.Caching;
using Portal.Infrastructure.Config;
using Portal.Infrastructure.Files;
using Portal.Infrastructure.Localization;
using Portal.Infrastructure.Messaging;
using Portal.Infrastructure.Pdf;

namespace Portal.Infrastructure;

/// <summary>Portal.Infrastructure 서비스 등록.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>설정·다국어 캐시, 캐시 버전 감시, 첨부 저장소, HTML→PDF 변환, 메일 발송(대기열 발송기는 <c>ISecretProtector</c>·<c>IPersonalDataProtector</c>가 필요하다)을 등록한다. <c>ICurrentUser</c>는 호스트가 등록한다.</summary>
    public static IServiceCollection AddPortalInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CacheOptions>().Bind(configuration.GetSection(CacheOptions.SectionName));
        services.AddSingleton<PortalConfig>();
        services.AddSingleton<IPortalConfig>(sp => sp.GetRequiredService<PortalConfig>());
        services.AddSingleton<ICacheVersionListener>(sp => sp.GetRequiredService<PortalConfig>());
        services.AddSingleton<LangResources>();
        services.AddSingleton<ICacheVersionListener>(sp => sp.GetRequiredService<LangResources>());
        services.AddScoped<IPortalLocalizer, PortalLocalizer>();
        services.AddHostedService<CacheVersionMonitor>();
        services.AddOptions<FileStorageOptions>().Bind(configuration.GetSection(FileStorageOptions.SectionName));
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddScoped<FileService>();
        services.AddSingleton<IMailTransport, SmtpMailTransport>();
        services.AddScoped<MailQueueSender>();
        services.AddScoped<NotificationService>();
        services.AddOptions<PdfOptions>().Bind(configuration.GetSection(PdfOptions.SectionName));
        services.AddSingleton<IPdfConverter, BrowserPdfConverter>();
        return services;
    }
}
