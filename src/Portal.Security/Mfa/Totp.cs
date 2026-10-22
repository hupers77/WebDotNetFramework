using System.Security.Cryptography;
using System.Text;

namespace Portal.Security.Mfa;

/// <summary>
/// TOTP (RFC 6238: HMAC-SHA1, 30초 단계, 6자리) 자체 구현 (설계서 5장). 인증 앱(Microsoft·Google Authenticator 등)이 모두 지원하는 기본값만 쓴다.
/// 시계 차이를 고려해 앞뒤 한 단계까지 받고, 이미 쓴 단계 이하의 코드는 받지 않는다(재사용 방지는 DB가 다시 확인).
/// </summary>
public static class Totp
{
    /// <summary>비밀키 길이 (RFC 4226 권장 160비트).</summary>
    public const int SecretSize = 20;

    /// <summary>코드 자릿수.</summary>
    public const int Digits = 6;

    /// <summary>시간 단계 (초).</summary>
    public const int StepSeconds = 30;

    /// <summary>앞뒤로 받는 단계 수.</summary>
    public const int Window = 1;

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>새 비밀키.</summary>
    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(SecretSize);

    /// <summary>시각의 시간 단계.</summary>
    public static long StepOf(DateTimeOffset now) => now.ToUnixTimeSeconds() / StepSeconds;

    /// <summary>단계의 코드 (앞자리 0 포함 6자리).</summary>
    public static string Code(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
#pragma warning disable CA5350 // RFC 6238 기본 알고리즘이고 인증 앱 호환을 위해 HMAC-SHA1을 쓴다 (HMAC 용도라 충돌 약점과 무관)
        HMACSHA1.HashData(secret, counter, hash);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 코드를 확인한다. 맞으면 맞은 단계, 틀렸거나 <paramref name="lastStep"/> 이하 단계면 null.
    /// 입력의 공백은 무시한다. 비교는 고정 시간으로 한다.
    /// </summary>
    public static long? Verify(byte[] secret, string? code, DateTimeOffset now, long? lastStep)
    {
        var digits = code?.Replace(" ", "", StringComparison.Ordinal);
        if (digits is not { Length: Digits } || !digits.All(char.IsAsciiDigit))
        {
            return null;
        }

        var input = Encoding.ASCII.GetBytes(digits);
        var current = StepOf(now);
        long? matched = null;
        for (var step = current - Window; step <= current + Window; step++)
        {
            // 모든 단계를 끝까지 비교해 맞은 위치가 시간으로 드러나지 않게 한다
            if (CryptographicOperations.FixedTimeEquals(input, Encoding.ASCII.GetBytes(Code(secret, step))) && step > (lastStep ?? long.MinValue))
            {
                matched ??= step;
            }
        }

        return matched;
    }

    /// <summary>Base32 (RFC 4648, 패딩 없음). 인증 앱에 직접 입력하는 등록 키 형식.</summary>
    public static string ToBase32(ReadOnlySpan<byte> data)
    {
        var result = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                result.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            result.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return result.ToString();
    }

    /// <summary>
    /// 인증 앱 등록 주소 (<c>otpauth://totp/...</c>, Google Authenticator Key URI 형식). QR 코드나 휴대폰 링크로 쓴다.
    /// </summary>
    public static string KeyUri(string issuer, string account, byte[] secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}"
        + $"?secret={ToBase32(secret)}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
}
