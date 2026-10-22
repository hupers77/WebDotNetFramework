using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Portal.Security.Secrets;

/// <summary>
/// 저장해야 하는 개인정보(업무용 이메일 등) 암호화 (설계서 9장 '개인정보 최소 저장'). 컬럼은 <c>..._ENC VARBINARY</c>로 둔다.
/// 설정 비밀값과 같은 Data Protection 키(AES-256 + HMAC)를 쓰되 용도 문자열을 달리해 서로 풀 수 없게 한다.
/// 검색·정렬이 필요한 값은 암호화하지 말고 저장 자체를 다시 검토한다.
/// </summary>
public interface IPersonalDataProtector
{
    /// <summary>평문을 DB에 저장할 바이트로 암호화한다. 같은 평문도 매번 다른 값이 나온다.</summary>
    byte[] Protect(string plainText);

    /// <summary>저장된 값을 평문으로 되돌린다. 키가 바뀌었거나 값이 손상되면 예외.</summary>
    string Unprotect(byte[] protectedData);
}

/// <inheritdoc />
internal sealed class DataProtectionPersonalDataProtector(IDataProtectionProvider provider) : IPersonalDataProtector
{
    /// <summary>용도 문자열. 바꾸면 기존 값을 읽을 수 없으므로 형식을 바꿀 때만 버전을 올린다.</summary>
    internal const string Purpose = "Portal.PersonalData.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    /// <inheritdoc />
    public byte[] Protect(string plainText) => _protector.Protect(Encoding.UTF8.GetBytes(plainText));

    /// <inheritdoc />
    public string Unprotect(byte[] protectedData) => Encoding.UTF8.GetString(_protector.Unprotect(protectedData));
}

/// <summary>화면에 보일 때 개인정보를 가린다.</summary>
public static class PersonalDataMask
{
    /// <summary>이메일: 앞 두 글자만 남긴다 (<c>ab***@example.com</c>). 형식이 아니면 모두 가린다.</summary>
    public static string Email(string? email)
    {
        var at = email?.LastIndexOf('@') ?? -1;
        if (email is null || at < 1)
        {
            return "***";
        }

        return string.Concat(email.AsSpan(0, Math.Min(2, at)), "***", email.AsSpan(at));
    }
}
