using Portal.Core.Context;
using Portal.Core.Localization;

namespace Portal.Infrastructure.Localization;

/// <summary><see cref="IPortalLocalizer"/> 구현. 언어는 현재 사용자(<see cref="ICurrentUser.LangCd"/>)를 따른다.</summary>
public sealed class PortalLocalizer(LangResources resources, ICurrentUser currentUser) : IPortalLocalizer
{
    /// <inheritdoc />
    public string this[string key] => resources.Get(currentUser.LangCd, key, []);

    /// <inheritdoc />
    public string this[string key, params object?[] args] => resources.Get(currentUser.LangCd, key, args);

    /// <inheritdoc />
    public string Get(string langCd, string key, params object?[] args) => resources.Get(langCd, key, args);
}
