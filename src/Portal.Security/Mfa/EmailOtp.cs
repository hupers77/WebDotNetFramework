using System.Security.Cryptography;
using System.Text;

namespace Portal.Security.Mfa;

/// <summary>
/// 이메일 인증번호를 메일로 보낸다 (구현은 메일 전송이 있는 웹 계층). 번호는 로그·대기열에 남기지 않는다.
/// 받는 주소는 암호문으로 받아 보낼 때만 푼다.
/// </summary>
public interface IMfaCodeMailer
{
    /// <summary>번호를 받는 사람의 언어로 메일을 보낸다. 보내지 못하면 예외를 던진다.</summary>
    Task SendAsync(byte[] toEmailEnc, string langCd, string code, CancellationToken cancellationToken = default);
}

/// <summary>이메일 인증번호 만들기·해시. DB에는 해시만 저장한다.</summary>
public static class EmailOtp
{
    /// <summary>번호 자릿수.</summary>
    public const int Length = 6;

    /// <summary>6자리 숫자 번호 (암호학적 난수).</summary>
    public static string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>번호가 6자리 숫자인지.</summary>
    public static bool IsWellFormed(string? code) => code is { Length: Length } && code.All(char.IsAsciiDigit);

    /// <summary>사용자별 해시 (다른 사용자의 같은 번호와 구분).</summary>
    public static byte[] Hash(string userId, string code) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"portal-otp:{userId}:{code}"));
}

/// <summary>메일 발송이 없는 호스트(Portal.Worker)의 기본값: 2단계 인증 화면이 없으므로 불리지 않는다. 웹이 실제 구현으로 바꾼다.</summary>
internal sealed class NoMfaCodeMailer : IMfaCodeMailer
{
    /// <inheritdoc />
    public Task SendAsync(byte[] toEmailEnc, string langCd, string code, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("이 호스트는 2단계 인증 메일을 보내지 않습니다.");
}
