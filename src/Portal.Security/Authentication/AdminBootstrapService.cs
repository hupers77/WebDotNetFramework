using System.Security.Cryptography;
using Portal.Data;
using Portal.Security.Passwords;

namespace Portal.Security.Authentication;

/// <summary>
/// 초기 관리자(<c>sysadmin</c>) 생성·비상 재설정 (초기 데이터 정의서 2장). 설치 도구가 호출하고,
/// 돌려받은 임시 비밀번호를 화면에 한 번만 보여 준다. 임시 비밀번호는 어디에도 저장하거나 기록하지 않는다.
/// </summary>
public sealed class AdminBootstrapService(ISpExecutor sp, IPasswordHasher hasher)
{
    private const int TempPasswordLength = 16;

    // 헷갈리는 문자(0/O, 1/l/I)는 뺀다
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%^&*-_=+?";

    /// <summary>관리자 계정을 만들거나 비밀번호를 재설정하고 임시 비밀번호를 돌려준다.</summary>
    public async Task<(bool Created, string TempPassword)> InitAsync(CancellationToken cancellationToken = default)
    {
        var password = GenerateTempPassword();
        var hash = hasher.Hash(password);
        var result = await sp.QuerySingleAsync<AdminInitResult>(
            AuthSp.AdminInit, new { P_PWD_HASH = hash.Hash, P_PWD_SALT = hash.Salt }, cancellationToken);
        return (result?.CreatedYn == "Y", password);
    }

    /// <summary>4종류 문자를 모두 포함하고 같은 문자가 연속되지 않는 임시 비밀번호.</summary>
    internal static string GenerateTempPassword()
    {
        const string all = Upper + Lower + Digits + Symbols;
        while (true)
        {
            var chars = RandomNumberGenerator.GetString(all, TempPasswordLength).ToCharArray();
            chars[0] = RandomNumberGenerator.GetString(Upper, 1)[0];
            chars[1] = RandomNumberGenerator.GetString(Lower, 1)[0];
            chars[2] = RandomNumberGenerator.GetString(Digits, 1)[0];
            chars[3] = RandomNumberGenerator.GetString(Symbols, 1)[0];
            RandomNumberGenerator.Shuffle(chars.AsSpan());
            var candidate = new string(chars);
            if (!HasAdjacentRepeat(candidate))
            {
                return candidate;
            }
        }
    }

    private static bool HasAdjacentRepeat(string value)
    {
        for (var i = 1; i < value.Length; i++)
        {
            if (value[i] == value[i - 1])
            {
                return true;
            }
        }

        return false;
    }
}
