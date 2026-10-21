using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Portal.Data;

/// <summary>Portal.Data 서비스 등록.</summary>
public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// SpExecutor를 등록한다. 연결 문자열은 <c>ConnectionStrings:Portal</c>에서 읽는다.
    /// <see cref="Portal.Core.Context.ICurrentUser"/>는 호스트(웹/워커)가 등록해야 한다.
    /// </summary>
    public static IServiceCollection AddPortalData(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SpExecutorOptions>().Bind(configuration.GetSection(SpExecutorOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISqlConnectionFactory>(sp =>
        {
            var name = sp.GetRequiredService<IOptions<SpExecutorOptions>>().Value.ConnectionStringName;
            var connectionString = configuration.GetConnectionString(name)
                ?? throw new InvalidOperationException($"연결 문자열 'ConnectionStrings:{name}'이 설정되지 않았습니다.");
            return new SqlConnectionFactory(connectionString);
        });
        services.TryAddScoped<ISpExecutor, SpExecutor>();
        return services;
    }
}
