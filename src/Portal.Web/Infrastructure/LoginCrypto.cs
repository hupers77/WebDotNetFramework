using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Portal.Web.Infrastructure;

/// <summary>
/// 로그인 입력(ID·비밀번호)을 브라우저에서 암호화해 보내기 위한 RSA-OAEP(SHA-256) 키.
/// 브라우저 개발자도구의 네트워크 탭·요청 본문에 ID/비밀번호가 평문으로 보이지 않게 한다(전송 구간 보호는 HTTPS가 맡는다).
/// 개인키는 메모리에만 두고 앱이 다시 시작되면 새로 만든다. 화면을 연 채 앱이 재시작되면 한 번 실패하고 다시 입력하면 된다.
/// 암호문에 시각을 넣어 5분이 지난 것은 받지 않는다.
/// </summary>
public sealed class LoginCrypto(TimeProvider time) : IDisposable
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    private readonly RSA _rsa = RSA.Create(2048);

    /// <summary>브라우저 WebCrypto가 가져갈 공개키 (SPKI, Base64).</summary>
    public string PublicKey => Convert.ToBase64String(_rsa.ExportSubjectPublicKeyInfo());

    /// <summary>
    /// 브라우저가 보낸 암호문(Base64)을 풀어 ID·비밀번호를 돌려준다. 형식이 틀리거나 오래되었으면 null.
    /// 예외 내용에는 입력값이 섞일 수 있어 밖으로 내보내지 않는다.
    /// </summary>
    public (string UserId, string Password)? TryDecrypt(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted) || encrypted.Length > 2048)
        {
            return null;
        }

        try
        {
            var plain = _rsa.Decrypt(Convert.FromBase64String(encrypted), RSAEncryptionPadding.OaepSHA256);
            var payload = JsonSerializer.Deserialize<Payload>(plain);
            if (payload?.U is not { } userId || payload.P is not { } password)
            {
                return null;
            }

            var age = time.GetUtcNow() - DateTimeOffset.FromUnixTimeMilliseconds(payload.T);
            return age < TimeSpan.Zero - TimeSpan.FromMinutes(1) || age > MaxAge ? null : (userId, password);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _rsa.Dispose();

    private sealed class Payload
    {
        [JsonPropertyName("u")]
        public string? U { get; set; }

        [JsonPropertyName("p")]
        public string? P { get; set; }

        [JsonPropertyName("t")]
        public long T { get; set; }
    }
}
