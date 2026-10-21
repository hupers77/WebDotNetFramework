using System.Text;
using Portal.Security.Passwords;

namespace Portal.UnitTests.Security;

/// <summary>빠른 테스트용 해시: 해시 = salt 뒤에 비밀번호 바이트. 비교 횟수를 센다(존재하지 않는 ID도 비교하는지 확인).</summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    public int VerifyCount { get; private set; }

    public static PasswordHash Make(string password) =>
        new([.. Encoding.UTF8.GetBytes(password).Concat(new byte[64]).Take(64)], new byte[16]);

    public PasswordHash Hash(string password) => Make(password);

    public bool Verify(string password, ReadOnlySpan<byte> hash, ReadOnlySpan<byte> salt)
    {
        VerifyCount++;
        return hash.SequenceEqual(Make(password).Hash);
    }
}
