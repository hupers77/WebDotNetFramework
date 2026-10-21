using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portal.Security.Authentication;
using Portal.Security.Authorization;
using Portal.Security.Passwords;

namespace Portal.Security;

/// <summary>보안 서비스 등록.</summary>
public static class SecurityServiceCollectionExtensions
{
    /// <summary>비밀번호 해시·정책, 로그인, 권한 평가 서비스를 등록한다. <c>IPortalConfig</c>, <c>ICurrentUser</c>, <c>ISpExecutor</c>가 필요하다.</summary>
    public static IServiceCollection AddPortalSecurity(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.TryAddScoped<PasswordPolicy>();
        services.TryAddScoped<AuthService>();
        services.TryAddScoped<PasswordService>();
        services.TryAddScoped<PermissionService>();
        services.TryAddScoped<AdminBootstrapService>();
        return services;
    }
}
