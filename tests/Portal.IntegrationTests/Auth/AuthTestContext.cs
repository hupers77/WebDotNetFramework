using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Portal.Core.Keys;
using Portal.IntegrationTests.Infrastructure;
using Portal.Security.Authentication;
using Portal.Security.Authorization;
using Portal.Security.Mfa;
using Portal.Security.Passwords;
using Portal.Testing;

namespace Portal.IntegrationTests.Auth;

/// <summary>실제 DB와 실제 PBKDF2로 인증 서비스를 만든다. 설정은 초기 데이터 값을 쓴다(테스트에서 TB_CONFIG를 바꾸면 SP에도 반영).</summary>
internal static class AuthTestContext
{
    public static readonly Pbkdf2PasswordHasher Hasher = new();

    /// <summary>테스트끼리 같은 키를 쓴다 (한 테스트에서 등록한 비밀키를 다시 읽을 수 있게).</summary>
    public static readonly DataProtectionMfaSecretProtector MfaSecrets = new(new EphemeralDataProtectionProvider());

    public static AuthService NewAuth(TestDb db, FakePortalConfig? config = null, TimeProvider? time = null, FakeMfaCodeMailer? mailer = null) =>
        new(db.Sp, Hasher, config ?? new FakePortalConfig(), db.User, time ?? TimeProvider.System, MfaSecrets, mailer ?? new FakeMfaCodeMailer(), NullLogger<AuthService>.Instance);

    /// <summary>2단계 인증을 끈 설정 (비밀번호 단계만 보는 테스트).</summary>
    public static FakePortalConfig PasswordOnly() =>
        new FakePortalConfig().Set(ConfigKeys.AuthPartnerMfa, "N").Set(ConfigKeys.AuthAdminMfa, "N");

    public static PasswordService NewPassword(TestDb db, FakePortalConfig? config = null)
    {
        var c = config ?? new FakePortalConfig();
        return new PasswordService(db.Sp, Hasher, new PasswordPolicy(c), c, db.User);
    }

    public static PermissionService NewPermission(TestDb db) => new(db.Sp, db.User);

    /// <summary>비밀번호가 있는 사용자를 만든다.</summary>
    public static async Task SeedLoginUserAsync(TestDb db, string userId, string password, string companyCd = "HQ", string userTypeCd = "EMP", params string[] roles)
    {
        await db.SeedUserAsync(userId, companyCd, userTypeCd, roles: roles);
        await SetPasswordAsync(db, userId, password);
    }

    public static Task<int> SetPasswordAsync(TestDb db, string userId, string password)
    {
        var hash = Hasher.Hash(password);
        return db.ExecAsync(
            "UPDATE FW.TB_USER SET PWD_HASH = @H, PWD_SALT = @S, PWD_CHG_DT = SYSUTCDATETIME() WHERE USER_ID = @U",
            ("@H", hash.Hash), ("@S", hash.Salt), ("@U", userId));
    }

    /// <summary>로그인 화면처럼 로그인 전 사용자로 바꾼다.</summary>
    public static void AsAnonymous(TestDb db, string clientIp = "10.1.2.3")
    {
        db.User.UserId = null;
        db.User.IsAuthenticated = false;
        db.User.ClientIp = clientIp;
    }
}
