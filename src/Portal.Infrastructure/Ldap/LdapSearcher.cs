using System.DirectoryServices.Protocols;
using System.Net;

namespace Portal.Infrastructure.Ldap;

/// <summary>연결 설정 (비밀번호가 들어 있으므로 기록하지 않는다).</summary>
internal sealed record LdapSettings(string Host, int Port, bool UseSsl, string BaseDn, string? BindDn, string? BindPassword, TimeSpan Timeout)
{
    public override string ToString() => $"{Host}:{Port}";
}

/// <summary>검색 결과 한 건.</summary>
internal sealed record LdapEntry(string Dn, IReadOnlyDictionary<string, IReadOnlyList<string>> Attributes)
{
    public string? First(string name) => Attributes.TryGetValue(name, out var v) && v.Count > 0 && v[0].Length > 0 ? v[0] : null;

    public IReadOnlyList<string> All(string name) => Attributes.TryGetValue(name, out var v) ? v : [];
}

/// <summary>LDAP 검색 실행 (서버 없이 테스트할 수 있도록 분리).</summary>
internal interface ILdapSearcher
{
    /// <summary>필터로 검색해 최대 <paramref name="maxResults"/>건을 돌려준다.</summary>
    Task<IReadOnlyList<LdapEntry>> SearchAsync(LdapSettings settings, string filter, string[] attributes, int maxResults, CancellationToken cancellationToken);
}

/// <summary>
/// <c>System.DirectoryServices.Protocols</c>로 검색한다. 조회 계정으로 Bind(없으면 익명)한 뒤 한 번 검색하고 연결을 닫는다.
/// 조회만 하며, 사용자 비밀번호를 받아 인증하지 않는다. 리퍼럴은 따라가지 않는다.
/// </summary>
internal sealed class LdapSearcher : ILdapSearcher
{
    private const int PageSize = 500;

    /// <inheritdoc />
    public Task<IReadOnlyList<LdapEntry>> SearchAsync(LdapSettings settings, string filter, string[] attributes, int maxResults, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<LdapEntry>>(() => Search(settings, filter, attributes, maxResults, cancellationToken), cancellationToken);

    private static List<LdapEntry> Search(LdapSettings settings, string filter, string[] attributes, int maxResults, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new LdapConnection(new LdapDirectoryIdentifier(settings.Host, settings.Port)) { Timeout = settings.Timeout };
            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.SecureSocketLayer = settings.UseSsl;
            connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            if (string.IsNullOrEmpty(settings.BindDn))
            {
                connection.AuthType = AuthType.Anonymous;
            }
            else
            {
                connection.AuthType = AuthType.Basic;
                connection.Credential = new NetworkCredential(settings.BindDn, settings.BindPassword);
            }

            connection.Bind();

            var results = new List<LdapEntry>();
            var paging = new PageResultRequestControl(Math.Min(PageSize, maxResults));
            while (results.Count < maxResults)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var request = new SearchRequest(settings.BaseDn, filter, SearchScope.Subtree, attributes) { TimeLimit = settings.Timeout };
                request.Controls.Add(paging);
                var response = (SearchResponse)connection.SendRequest(request);
                foreach (SearchResultEntry entry in response.Entries)
                {
                    if (results.Count >= maxResults)
                    {
                        break;
                    }

                    results.Add(ToEntry(entry));
                }

                var page = response.Controls.OfType<PageResultResponseControl>().FirstOrDefault();
                if (page is null || page.Cookie.Length == 0)
                {
                    break;
                }

                paging.Cookie = page.Cookie;
            }

            return results;
        }
        catch (Exception ex) when (ex is LdapException or DirectoryOperationException or DirectoryException or InvalidOperationException or PlatformNotSupportedException)
        {
            // 서버 주소·계정이 메시지에 섞이지 않도록 원인만 안쪽 예외로 보존한다
            throw new LdapQueryException("LDAP 서버에서 조회하지 못했습니다. 서버 설정·계정·네트워크를 확인하세요.", ex);
        }
    }

    private static LdapEntry ToEntry(SearchResultEntry entry)
    {
        var attributes = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in entry.Attributes.AttributeNames)
        {
            attributes[name] = [.. entry.Attributes[name].GetValues(typeof(string)).Cast<string>()];
        }

        return new LdapEntry(entry.DistinguishedName, attributes);
    }
}
