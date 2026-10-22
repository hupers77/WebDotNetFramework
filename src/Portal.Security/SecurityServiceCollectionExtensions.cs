using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portal.Core.Security;
using Portal.Security.Authentication;
using Portal.Security.Authorization;
using Portal.Security.Mfa;
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
        services.TryAddSingleton<IMfaSecretProtector, DataProtectionMfaSecretProtector>();
        services.TryAddSingleton<MfaTicketProtector>();
        return services;
    }

    /// <summary>
    /// Data Protection 키 위치를 정한다(CSRF 토큰·설정 비밀값·개인정보·2단계 인증 비밀키 암호화). 웹과 Portal.Worker가 같은 값을 써야
    /// 서로 암호화한 값을 풀 수 있다: 애플리케이션 이름 <c>Portal</c>과 같은 키 폴더(<c>Portal:DataProtection:KeyPath</c>).
    /// 운영은 백업되는 로컬 폴더에 두고 Windows DPAPI(컴퓨터 단위)로 보호한다(설계서 3장·16장). 경로를 비우면 .NET 기본 위치(사용자 프로필)를 쓴다(개발 PC).
    /// </summary>
    public static IServiceCollection AddPortalDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        var dataProtection = services.AddDataProtection().SetApplicationName("Portal");
        if (configuration["Portal:DataProtection:KeyPath"] is { Length: > 0 } keyPath)
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
            if (OperatingSystem.IsWindows())
            {
                dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
            }
        }

        return services;
    }
}
