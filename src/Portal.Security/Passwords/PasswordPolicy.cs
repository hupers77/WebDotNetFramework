using Portal.Core.Config;
using Portal.Core.Keys;

namespace Portal.Security.Passwords;

/// <summary>
/// 비밀번호 규칙 (설계서 5장, 설정 <c>PWD.*</c>): 문자 종류 3가지 이상이면 <c>PWD.MIN_LEN_3TYPES</c>자,
/// 2가지면 <c>PWD.MIN_LEN_2TYPES</c>자 이상. 사용자 ID 포함, 같은 문자 4번 이상 연속은 금지.
/// 최근 비밀번호 재사용 검사는 해시가 필요하므로 <see cref="Authentication.PasswordService"/>가 한다.
/// </summary>
public sealed class PasswordPolicy(IPortalConfig config)
{
    /// <summary>최대 길이. 긴 입력으로 해시 계산을 지연시키는 공격을 막는다.</summary>
    public const int MaxLength = 128;

    /// <summary>위반한 규칙 목록 (화면에 모두 보여 준다). 비어 있으면 통과.</summary>
    public IReadOnlyList<PasswordViolation> Validate(string password, string userId)
    {
        var errors = new List<PasswordViolation>();
        if (password.Length > MaxLength)
        {
            errors.Add(new(PasswordMessageKeys.TooLong, [MaxLength]));
            return errors;
        }

        var types = CountTypes(password);
        var min3 = config.GetInt(ConfigKeys.PwdMinLen3types);
        var min2 = config.GetInt(ConfigKeys.PwdMinLen2types);
        var lengthOk = (types >= 3 && password.Length >= min3) || (types == 2 && password.Length >= min2);
        if (!lengthOk)
        {
            errors.Add(new(PasswordMessageKeys.Weak, [min3, min2]));
        }

        if (userId.Length >= 3 && password.Contains(userId, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new(PasswordMessageKeys.ContainsUserId, []));
        }

        if (HasRepeat(password, 4))
        {
            errors.Add(new(PasswordMessageKeys.Repeated, []));
        }

        return errors;
    }

    internal static int CountTypes(string password)
    {
        bool upper = false, lower = false, digit = false, special = false;
        foreach (var ch in password)
        {
            if (char.IsUpper(ch))
            {
                upper = true;
            }
            else if (char.IsLower(ch))
            {
                lower = true;
            }
            else if (char.IsDigit(ch))
            {
                digit = true;
            }
            else
            {
                special = true;
            }
        }

        return (upper ? 1 : 0) + (lower ? 1 : 0) + (digit ? 1 : 0) + (special ? 1 : 0);
    }

    private static bool HasRepeat(string password, int count)
    {
        var run = 1;
        for (var i = 1; i < password.Length; i++)
        {
            run = password[i] == password[i - 1] ? run + 1 : 1;
            if (run >= count)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>비밀번호 규칙 위반 하나.</summary>
/// <param name="MessageKey">다국어 메시지 키 (<see cref="PasswordMessageKeys"/>).</param>
/// <param name="Args">메시지 인자.</param>
public sealed record PasswordViolation(string MessageKey, IReadOnlyList<object?> Args);

/// <summary>비밀번호 규칙 메시지 키.</summary>
public static class PasswordMessageKeys
{
    /// <summary>문자 종류·길이 조건 미달. 인자: 3종류 최소 길이, 2종류 최소 길이.</summary>
    public const string Weak = "VAL.PWD_WEAK";

    /// <summary>사용자 ID 포함.</summary>
    public const string ContainsUserId = "VAL.PWD_CONTAINS_ID";

    /// <summary>같은 문자 반복.</summary>
    public const string Repeated = "VAL.PWD_REPEATED";

    /// <summary>현재 또는 최근 사용한 비밀번호. 인자: 이력 수.</summary>
    public const string Reused = "VAL.PWD_REUSED";

    /// <summary>현재 비밀번호 불일치.</summary>
    public const string CurrentMismatch = "VAL.PWD_CURRENT_MISMATCH";

    /// <summary>최대 길이 초과. 인자: 최대 길이.</summary>
    public const string TooLong = "VAL.PWD_TOO_LONG";

    /// <summary>새 비밀번호와 확인 값 불일치 (화면에서 검사).</summary>
    public const string ConfirmMismatch = "VAL.PWD_CONFIRM_MISMATCH";
}
