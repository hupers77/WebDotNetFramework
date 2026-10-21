using System.Security.Cryptography;

namespace Portal.Security.Passwords;

/// <summary>
/// PBKDF2-HMAC-SHA512, 210,000회, 16바이트 salt, 64바이트 출력 (설계서 5장, OWASP 권장값).
/// 반복 횟수를 올리려면 해시에 버전을 붙이는 마이그레이션이 필요하므로 상수로 고정한다(보안상 낮추면 안 되는 값).
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    internal const int Iterations = 210_000;
    internal const int SaltSize = 16;
    internal const int HashSize = 64;

    /// <inheritdoc />
    public PasswordHash Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        return new PasswordHash(Derive(password, salt), salt);
    }

    /// <inheritdoc />
    public bool Verify(string password, ReadOnlySpan<byte> hash, ReadOnlySpan<byte> salt)
    {
        if (hash.Length != HashSize || salt.Length != SaltSize)
        {
            return false;
        }

        Span<byte> actual = stackalloc byte[HashSize];
        Rfc2898DeriveBytes.Pbkdf2(password, salt, actual, Iterations, HashAlgorithmName.SHA512);
        return CryptographicOperations.FixedTimeEquals(actual, hash);
    }

    private static byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA512, HashSize);
}
