using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Portal.Core.Security;

namespace Portal.Security.Secrets;

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
