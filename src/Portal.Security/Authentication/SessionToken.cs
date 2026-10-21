using System.Security.Cryptography;
using System.Text;

namespace Portal.Security.Authentication;

/// <summary>
/// 세션 토큰. 무작위 256비트 값을 쿠키에만 두고, DB(<c>TB_USER_SESSION.SESSION_ID</c>)에는 SHA-256 해시만 저장한다.
/// DB가 유출되어도 해시로는 세션을 가로챌 수 없다.
/// </summary>
public static class SessionToken
{
    private const int TokenBytes = 32;

    /// <summary>새 토큰(쿠키 값)과 그 해시(DB 키).</summary>
    public static (string Token, string Hash) Create()
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(TokenBytes));
        return (token, Hash(token));
    }

    /// <summary>쿠키 값의 해시 (소문자 16진수 64자).</summary>
    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    /// <summary>쿠키 값 형식 검사 (임의 문자열로 DB를 조회하지 않도록).</summary>
    public static bool IsWellFormed(string? token) =>
        token is { Length: 43 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
