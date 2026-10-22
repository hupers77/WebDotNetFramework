using Microsoft.AspNetCore.DataProtection;
using Portal.Core.Security;

namespace Portal.Security.Secrets;

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
