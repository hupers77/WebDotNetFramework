using Microsoft.AspNetCore.DataProtection;

namespace Portal.Security.Secrets;

/// <summary>
/// 설정 비밀값(<c>VAL_TYPE_CD = SECRET</c>: SMTP·AD 계정 비밀번호 등) 암호화 (설계서 9장 '설정·비밀값').
/// ASP.NET Core Data Protection(AES-256 + HMAC)을 쓰고, 키는 웹서버 로컬 폴더에 두며 Windows에서는 DPAPI로 보호한다.
/// 키를 잃으면 비밀값을 다시 입력해야 하므로 키 폴더는 백업 대상이다(설계서 16장).
/// </summary>
public interface ISecretProtector
{
    /// <summary>평문을 DB에 저장할 문자열로 암호화한다. 같은 평문도 매번 다른 값이 나온다.</summary>
    string Protect(string plainText);

    /// <summary>저장된 값을 평문으로 되돌린다. 키가 바뀌었거나 값이 손상되면 예외.</summary>
    string Unprotect(string protectedText);
}

/// <inheritdoc />
internal sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    /// <summary>용도 문자열. 바꾸면 기존 비밀값을 읽을 수 없으므로 형식을 바꿀 때만 버전을 올린다.</summary>
    internal const string Purpose = "Portal.Config.Secret.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    /// <inheritdoc />
    public string Protect(string plainText) => _protector.Protect(plainText);

    /// <inheritdoc />
    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
}
