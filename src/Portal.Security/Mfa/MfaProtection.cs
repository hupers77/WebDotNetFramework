using Microsoft.AspNetCore.DataProtection;

namespace Portal.Security.Mfa;

/// <summary>TOTP 비밀키 암호화 (<c>TB_USER.MFA_SECRET_ENC</c>). 설정 비밀값·개인정보와 다른 용도 키를 쓴다.</summary>
public interface IMfaSecretProtector
{
    /// <summary>비밀키를 DB에 저장할 바이트로 암호화한다.</summary>
    byte[] Protect(byte[] secret);

    /// <summary>저장된 값을 비밀키로 되돌린다. 키가 바뀌었거나 값이 손상되면 예외.</summary>
    byte[] Unprotect(byte[] protectedSecret);
}

/// <inheritdoc />
internal sealed class DataProtectionMfaSecretProtector(IDataProtectionProvider provider) : IMfaSecretProtector
{
    /// <summary>용도 문자열. 바꾸면 등록한 인증 앱을 모두 다시 등록해야 한다.</summary>
    internal const string Purpose = "Portal.Mfa.Secret.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    /// <inheritdoc />
    public byte[] Protect(byte[] secret) => _protector.Protect(secret);

    /// <inheritdoc />
    public byte[] Unprotect(byte[] protectedSecret) => _protector.Unprotect(protectedSecret);
}

/// <summary>
/// 2단계 인증 대기표: 비밀번호를 확인한 뒤 코드를 받을 때까지 브라우저 쿠키에 두는 값.
/// 처음 등록하는 계정은 확인 전까지 새 비밀키를 DB에 저장하지 않고 이 안에(암호화해) 둔다.
/// </summary>
/// <param name="UserId">비밀번호를 확인한 사용자.</param>
/// <param name="PendingSecret">등록 중인 새 비밀키. 이미 등록한 계정이면 null.</param>
public sealed record MfaTicket(string UserId, byte[]? PendingSecret);

/// <summary>대기표 암호화 (만료 시각 포함). 만료되었거나 바뀐 값은 null로 돌려준다.</summary>
public sealed class MfaTicketProtector(IDataProtectionProvider provider)
{
    /// <summary>대기표 유효 시간: 이 안에 코드를 입력하지 않으면 비밀번호부터 다시 입력한다.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    internal const string Purpose = "Portal.Mfa.Ticket.v1";

    private readonly ITimeLimitedDataProtector _protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    /// <summary>쿠키에 담을 문자열.</summary>
    public string Protect(MfaTicket ticket)
    {
        var secret = ticket.PendingSecret is null ? "" : Convert.ToBase64String(ticket.PendingSecret);
        return _protector.Protect($"{ticket.UserId}\n{secret}", Lifetime);
    }

    /// <summary>쿠키 값을 대기표로 되돌린다. 없거나, 만료되었거나, 위조되었으면 null.</summary>
    public MfaTicket? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            var parts = _protector.Unprotect(value).Split('\n');
            return parts is [{ Length: > 0 } userId, var secret]
                ? new MfaTicket(userId, secret.Length == 0 ? null : Convert.FromBase64String(secret))
                : null;
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
        {
            return null;
        }
    }
}
