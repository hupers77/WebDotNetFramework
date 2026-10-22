using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portal.Security.Authentication;
using Portal.Security.Authorization;
using Portal.Security.Passwords;
using Portal.Security.Secrets;

namespace Portal.Security;

/// <summary>보안 서비스 등록.</summary>
public static class SecurityServiceCollectionExtensions
{
    /// <summary>
    /// 비밀번호 해시·정책, 로그인, 협력사 가입 신청, 권한 평가, 설정 비밀값·개인정보 암호화 서비스를 등록한다. <c>IPortalConfig</c>, <c>ICurrentUser</c>, <c>ISpExecutor</c>가 필요하다.
    /// Data Protection 키 저장 위치는 호스트가 정한다(웹: <c>Portal:DataProtection:KeyPath</c>).
    /// </summary>
    public static IServiceCollection AddPortalSecurity(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.TryAddScoped<PasswordPolicy>();
        services.TryAddScoped<AuthService>();
        services.TryAddScoped<PasswordService>();
        services.TryAddScoped<PermissionService>();
        services.TryAddScoped<AdminBootstrapService>();
        services.AddDataProtection();
        services.TryAddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.TryAddSingleton<IPersonalDataProtector, DataProtectionPersonalDataProtector>();
        services.TryAddScoped<PartnerJoinService>();
        return services;
    }
}
