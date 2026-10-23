using System.Text;
using Portal.Core.Config;
using Portal.Core.Keys;
using Portal.Core.Security;

namespace Portal.Infrastructure.Ldap;

/// <summary>디렉터리(AD)에서 읽은 사용자. 메모리에서만 쓰고 이 모듈은 어디에도 저장하지 않는다(개인정보는 저장하지 않는 것이 원칙).</summary>
/// <param name="Account">로그인 계정 (<c>sAMAccountName</c>).</param>
/// <param name="DisplayName">표시 이름.</param>
/// <param name="Email">메일 주소 (없을 수 있다).</param>
/// <param name="Department">부서 이름 (없을 수 있다).</param>
/// <param name="DistinguishedName">DN.</param>
/// <param name="Disabled">AD에서 비활성화된 계정인지 (<c>userAccountControl</c>의 ACCOUNTDISABLE).</param>
/// <param name="Groups">소속 그룹 이름 (<c>memberOf</c>의 CN).</param>
public sealed record LdapUser(
    string Account,
    string DisplayName,
    string? Email,
    string? Department,
    string DistinguishedName,
    bool Disabled,
    IReadOnlyList<string> Groups);

/// <summary>LDAP 설정(<c>LDAP.*</c>)이 꺼져 있거나 비어 있을 때.</summary>
public sealed class LdapNotConfiguredException(string message) : InvalidOperationException(message);

/// <summary>디렉터리에 연결하지 못했거나 조회에 실패했을 때. 메시지에는 서버 주소·계정·비밀번호를 담지 않는다.</summary>
public sealed class LdapQueryException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// LDAP 조회 모듈 (읽기 전용). 사용자·조직을 포털 테이블로 옮기는 방식(AD 마스터 뷰, LDAP 등)은 업무 사이트마다 달라서
/// 프레임워크는 조회 도구만 제공하고, 동기화·퇴사자 잠금 로직은 각 업무 개발에서 이 모듈이나 다른 원본으로 구현한다(2026-10-23 결정).
/// 포털은 AD 비밀번호를 받거나 확인하지 않는다(LDAP Bind 인증 없음).
/// </summary>
public interface ILdapDirectory
{
    /// <summary>설정에서 사용으로 켜져 있는지.</summary>
    bool Enabled { get; }

    /// <summary>계정 하나를 찾는다. 없으면 null.</summary>
    /// <exception cref="LdapNotConfiguredException">꺼져 있거나 설정이 비어 있을 때.</exception>
    /// <exception cref="LdapQueryException">연결·조회에 실패했을 때.</exception>
    Task<LdapUser?> FindUserAsync(string account, CancellationToken cancellationToken = default);

    /// <summary>계정 앞부분 또는 이름 일부로 사용자를 검색한다 (최대 <paramref name="maxResults"/>명, 1~1000).</summary>
    /// <exception cref="LdapNotConfiguredException">꺼져 있거나 설정이 비어 있을 때.</exception>
    /// <exception cref="LdapQueryException">연결·조회에 실패했을 때.</exception>
    Task<IReadOnlyList<LdapUser>> SearchUsersAsync(string keyword, int maxResults = 50, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class LdapDirectory(IPortalConfig config, ISecretProtector secrets, ILdapSearcher searcher) : ILdapDirectory
{
    internal const int MaxResultsLimit = 1000;
    internal const int AccountDisableFlag = 0x2;
    internal static readonly string[] UserAttributes = ["sAMAccountName", "displayName", "mail", "department", "memberOf", "userAccountControl"];

    /// <inheritdoc />
    public bool Enabled => config.GetBool(ConfigKeys.LdapEnabled);

    /// <inheritdoc />
    public async Task<LdapUser?> FindUserAsync(string account, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        var filter = ApplyFilter(config.GetString(ConfigKeys.LdapUserFilter), account.Trim());
        var entries = await searcher.SearchAsync(ReadSettings(), filter, UserAttributes, 2, cancellationToken);
        return entries.Select(ToUser).FirstOrDefault(u => u is not null);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LdapUser>> SearchUsersAsync(string keyword, int maxResults = 50, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        var filter = ApplyFilter(config.GetString(ConfigKeys.LdapUserSearchFilter), keyword.Trim());
        var entries = await searcher.SearchAsync(ReadSettings(), filter, UserAttributes, Math.Clamp(maxResults, 1, MaxResultsLimit), cancellationToken);
        return [.. entries.Select(ToUser).OfType<LdapUser>()];
    }

    /// <summary>필터 서식의 <c>{0}</c> 자리에 값을 넣는다. 값은 RFC 4515 규칙으로 이스케이프해 필터를 바꿀 수 없게 한다(LDAP 인젝션 방지).</summary>
    internal static string ApplyFilter(string? template, string value)
    {
        if (string.IsNullOrWhiteSpace(template) || !template.Contains("{0}", StringComparison.Ordinal))
        {
            throw new LdapNotConfiguredException("LDAP 필터 설정에 {0}이(가) 없습니다.");
        }

        return template.Replace("{0}", Escape(value), StringComparison.Ordinal);
    }

    /// <summary>LDAP 필터 값 이스케이프 (<c>\ * ( )</c>와 NUL).</summary>
    internal static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\5c"); break;
                case '*': sb.Append("\\2a"); break;
                case '(': sb.Append("\\28"); break;
                case ')': sb.Append("\\29"); break;
                case '\0': sb.Append("\\00"); break;
                default: sb.Append(ch); break;
            }
        }

        return sb.ToString();
    }

    /// <summary>DN의 첫 CN 값 (이스케이프된 쉼표 <c>\,</c> 처리). 예: <c>CN=Sales\, EU,OU=Groups</c> → <c>Sales, EU</c></summary>
    internal static string? ParseCn(string distinguishedName)
    {
        if (!distinguishedName.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var sb = new StringBuilder();
        for (var i = 3; i < distinguishedName.Length; i++)
        {
            var ch = distinguishedName[i];
            if (ch == '\\' && i + 1 < distinguishedName.Length)
            {
                sb.Append(distinguishedName[++i]);
            }
            else if (ch == ',')
            {
                break;
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    private static LdapUser? ToUser(LdapEntry entry)
    {
        var account = entry.First("sAMAccountName");
        if (account is null)
        {
            return null;
        }

        var disabled = int.TryParse(entry.First("userAccountControl"), out var flags) && (flags & AccountDisableFlag) != 0;
        var groups = entry.All("memberOf").Select(ParseCn).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList();
        return new LdapUser(account, entry.First("displayName") ?? account, entry.First("mail"), entry.First("department"), entry.Dn, disabled, groups);
    }

    private LdapSettings ReadSettings()
    {
        if (!Enabled)
        {
            throw new LdapNotConfiguredException("LDAP 조회 모듈이 꺼져 있습니다 (설정 LDAP.ENABLED).");
        }

        var host = config.GetString(ConfigKeys.LdapHost) ?? throw new LdapNotConfiguredException("설정 LDAP.HOST가 비어 있습니다.");
        var baseDn = config.GetString(ConfigKeys.LdapBaseDn) ?? throw new LdapNotConfiguredException("설정 LDAP.BASE_DN이 비어 있습니다.");
        var bindDn = config.GetString(ConfigKeys.LdapBindDn);
        string? password;
        try
        {
            password = config.GetString(ConfigKeys.LdapBindPassword) is { } encrypted ? secrets.Unprotect(encrypted) : null;
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
        {
            throw new LdapNotConfiguredException("LDAP.BIND_PASSWORD를 읽을 수 없습니다. 설정 관리 화면에서 다시 입력하세요.");
        }

        return new LdapSettings(host, config.GetInt(ConfigKeys.LdapPort), config.GetBool(ConfigKeys.LdapUseSsl), baseDn, bindDn, password,
            TimeSpan.FromSeconds(config.GetInt(ConfigKeys.LdapTimeoutSec)));
    }
}
