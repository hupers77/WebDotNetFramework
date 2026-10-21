namespace Portal.Security.Passwords;

/// <summary>비밀번호 해시. 원문은 어디에도 저장하거나 기록하지 않는다.</summary>
public interface IPasswordHasher
{
    /// <summary>새 salt로 해시를 만든다.</summary>
    PasswordHash Hash(string password);

    /// <summary>저장된 해시와 고정 시간으로 비교한다.</summary>
    bool Verify(string password, ReadOnlySpan<byte> hash, ReadOnlySpan<byte> salt);
}

/// <summary>저장할 해시와 salt (<c>TB_USER.PWD_HASH</c>, <c>PWD_SALT</c>).</summary>
/// <param name="Hash">64바이트 해시.</param>
/// <param name="Salt">16바이트 salt.</param>
public sealed record PasswordHash(byte[] Hash, byte[] Salt);
