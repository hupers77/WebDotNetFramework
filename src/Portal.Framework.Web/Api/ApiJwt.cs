using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Portal.Framework.Web.Api;

/// <summary>
/// 서명 토큰(JWT, HS256). 알고리즘을 하나로 고정하고(헤더가 우리가 만든 것과 다르면 거부) 서명은 고정 시간으로 비교한다.
/// 토큰에는 클라이언트 ID(sub)와 허용 API 코드(scope, 공백 구분)만 담는다.
/// </summary>
internal sealed class ApiJwt(byte[] key, string issuer, string audience, TimeProvider time)
{
    private static readonly byte[] HeaderJson = Encoding.ASCII.GetBytes("""{"alg":"HS256","typ":"JWT"}""");
    private static readonly string HeaderPart = Base64Url(HeaderJson);
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    /// <summary>토큰을 만든다.</summary>
    public string Create(string clientId, IEnumerable<string> apiCodes, TimeSpan lifetime, out DateTimeOffset expires)
    {
        var now = time.GetUtcNow();
        expires = now + lifetime;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["iss"] = issuer,
            ["aud"] = audience,
            ["sub"] = clientId,
            ["scope"] = string.Join(' ', apiCodes),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = expires.ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N"),
        });
        var unsigned = HeaderPart + "." + Base64Url(payload);
        return unsigned + "." + Base64Url(Sign(unsigned));
    }

    /// <summary>토큰을 확인한다. 서명·형식·발급자·대상·만료 중 하나라도 어긋나면 null.</summary>
    public ApiCaller? Validate(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 4096)
        {
            return null;
        }

        var parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != HeaderPart)
        {
            return null;
        }

        try
        {
            var signature = FromBase64Url(parts[2]);
            if (!CryptographicOperations.FixedTimeEquals(signature, Sign(parts[0] + "." + parts[1])))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(FromBase64Url(parts[1]));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || GetString(root, "iss") != issuer || GetString(root, "aud") != audience
                || GetString(root, "sub") is not { Length: > 0 } clientId
                || !root.TryGetProperty("exp", out var exp) || !exp.TryGetInt64(out var expSeconds))
            {
                return null;
            }

            if (DateTimeOffset.FromUnixTimeSeconds(expSeconds) + ClockSkew < time.GetUtcNow())
            {
                return null;
            }

            var codes = (GetString(root, "scope") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return new ApiCaller(clientId, new HashSet<string>(codes, StringComparer.Ordinal));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private byte[] Sign(string unsigned) => HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(unsigned));

    internal static string Base64Url(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '=');
        return Convert.FromBase64String(s);
    }
}
