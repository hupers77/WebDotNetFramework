using System.Security.Cryptography;
using Portal.Core.Config;
using Portal.Core.Keys;
using Portal.Core.Security;
using Portal.Infrastructure.Ldap;

namespace Portal.UnitTests.Infrastructure;

public sealed class LdapDirectoryTests
{
    [Theory]
    [InlineData("kim", "kim")]
    [InlineData("a*b", "a\\2ab")]
    [InlineData("x)(uid=*", "x\\29\\28uid=\\2a")]
    [InlineData("a\\b", "a\\5cb")]
    public void Escape_는_필터_특수문자를_바꾼다(string input, string expected) =>
        Assert.Equal(expected, LdapDirectory.Escape(input));

    [Fact]
    public void ApplyFilter_는_값을_이스케이프해_넣는다() =>
        Assert.Equal("(sAMAccountName=a\\2a)", LdapDirectory.ApplyFilter("(sAMAccountName={0})", "a*"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("(cn=fixed)")]
    public void ApplyFilter_는_자리표시자가_없으면_설정_오류(string? template) =>
        Assert.Throws<LdapNotConfiguredException>(() => LdapDirectory.ApplyFilter(template, "x"));

    [Theory]
    [InlineData("CN=Sales,OU=Groups,DC=corp", "Sales")]
    [InlineData("CN=Sales\\, EU,OU=Groups", "Sales, EU")]
    [InlineData("OU=Groups,DC=corp", null)]
    [InlineData("CN=,DC=corp", null)]
    public void ParseCn_은_첫_CN을_읽는다(string dn, string? expected) =>
        Assert.Equal(expected, LdapDirectory.ParseCn(dn));

    [Fact]
    public async Task 꺼져_있으면_조회하지_않고_예외()
    {
        var (dir, searcher) = Create(new() { [ConfigKeys.LdapEnabled] = "N" });

        await Assert.ThrowsAsync<LdapNotConfiguredException>(() => dir.FindUserAsync("kim"));
        Assert.Equal(0, searcher.Calls);
    }

    [Fact]
    public async Task 호스트가_비어_있으면_설정_오류()
    {
        var (dir, _) = Create(Settings(host: null));

        await Assert.ThrowsAsync<LdapNotConfiguredException>(() => dir.FindUserAsync("kim"));
    }

    [Fact]
    public async Task 조회_계정_비밀번호는_복호화해_전달한다()
    {
        var (dir, searcher) = Create(Settings());

        await dir.FindUserAsync("kim");

        Assert.Equal("pw", searcher.LastSettings!.BindPassword);
        Assert.Equal("ldap.corp", searcher.LastSettings.Host);
        Assert.DoesNotContain("pw", searcher.LastSettings.ToString());
    }

    [Fact]
    public async Task 비밀번호를_복호화할_수_없으면_설정_오류()
    {
        var (dir, _) = Create(Settings(password: "broken"));

        await Assert.ThrowsAsync<LdapNotConfiguredException>(() => dir.FindUserAsync("kim"));
    }

    [Fact]
    public async Task 사용자를_읽어_그룹과_비활성_여부를_채운다()
    {
        var (dir, searcher) = Create(Settings());
        searcher.Entries.Add(Entry("CN=Kim,OU=U", ("sAMAccountName", ["kim"]), ("displayName", ["Kim"]), ("mail", ["k@x.com"]), ("department", ["IT"]),
            ("userAccountControl", ["514"]), ("memberOf", ["CN=Zeta,OU=G", "CN=Alpha\\, Co,OU=G"])));

        var user = await dir.FindUserAsync(" kim ");

        Assert.NotNull(user);
        Assert.Equal("kim", user.Account);
        Assert.True(user.Disabled);
        Assert.Equal(["Alpha, Co", "Zeta"], user.Groups);
        Assert.Contains("(sAMAccountName=kim)", searcher.LastFilter);
    }

    [Fact]
    public async Task 활성_계정이고_표시이름이_없으면_계정을_쓴다()
    {
        var (dir, searcher) = Create(Settings());
        searcher.Entries.Add(Entry("CN=Lee,OU=U", ("sAMAccountName", ["lee"]), ("userAccountControl", ["512"])));

        var user = await dir.FindUserAsync("lee");

        Assert.False(user!.Disabled);
        Assert.Equal("lee", user.DisplayName);
        Assert.Null(user.Email);
    }

    [Fact]
    public async Task 없으면_null() => Assert.Null(await Create(Settings()).Dir.FindUserAsync("none"));

    [Fact]
    public async Task 검색_건수는_1에서_1000으로_맞춘다()
    {
        var (dir, searcher) = Create(Settings());

        await dir.SearchUsersAsync("k", 5000);
        Assert.Equal(1000, searcher.LastMax);
        await dir.SearchUsersAsync("k", 0);
        Assert.Equal(1, searcher.LastMax);
    }

    [Fact]
    public async Task 계정이_없는_항목은_검색_결과에서_뺀다()
    {
        var (dir, searcher) = Create(Settings());
        searcher.Entries.Add(Entry("CN=X", ("displayName", ["no account"])));
        searcher.Entries.Add(Entry("CN=Y", ("sAMAccountName", ["y"])));

        var users = await dir.SearchUsersAsync("y");

        Assert.Single(users);
    }

    private static Dictionary<string, string?> Settings(string? host = "ldap.corp", string password = "enc:pw") => new()
    {
        [ConfigKeys.LdapEnabled] = "Y",
        [ConfigKeys.LdapHost] = host,
        [ConfigKeys.LdapPort] = "636",
        [ConfigKeys.LdapUseSsl] = "Y",
        [ConfigKeys.LdapBaseDn] = "DC=corp",
        [ConfigKeys.LdapBindDn] = "CN=svc",
        [ConfigKeys.LdapBindPassword] = password,
        [ConfigKeys.LdapUserFilter] = "(sAMAccountName={0})",
        [ConfigKeys.LdapUserSearchFilter] = "(sAMAccountName={0}*)",
        [ConfigKeys.LdapTimeoutSec] = "15",
    };

    private static (LdapDirectory Dir, FakeSearcher Searcher) Create(Dictionary<string, string?> values)
    {
        var searcher = new FakeSearcher();
        return (new LdapDirectory(new FakeConfig(values), new FakeSecrets(), searcher), searcher);
    }

    private static LdapEntry Entry(string dn, params (string Name, string[] Values)[] attrs) =>
        new(dn, attrs.ToDictionary(a => a.Name, a => (IReadOnlyList<string>)a.Values));

    private sealed class FakeConfig(Dictionary<string, string?> values) : IPortalConfig
    {
        public string? GetString(string key) => values.GetValueOrDefault(key) is { Length: > 0 } v ? v : null;

        public int GetInt(string key) => int.Parse(values[key]!, System.Globalization.CultureInfo.InvariantCulture);

        public bool GetBool(string key) => values.GetValueOrDefault(key) == "Y";
    }

    private sealed class FakeSecrets : ISecretProtector
    {
        public string Protect(string plainText) => "enc:" + plainText;

        public string Unprotect(string protectedText) =>
            protectedText.StartsWith("enc:", StringComparison.Ordinal) ? protectedText[4..] : throw new CryptographicException("bad");
    }

    private sealed class FakeSearcher : ILdapSearcher
    {
        public List<LdapEntry> Entries { get; } = [];

        public int Calls { get; private set; }

        public LdapSettings? LastSettings { get; private set; }

        public string? LastFilter { get; private set; }

        public int LastMax { get; private set; }

        public Task<IReadOnlyList<LdapEntry>> SearchAsync(LdapSettings settings, string filter, string[] attributes, int maxResults, CancellationToken cancellationToken)
        {
            Calls++;
            LastSettings = settings;
            LastFilter = filter;
            LastMax = maxResults;
            return Task.FromResult<IReadOnlyList<LdapEntry>>(Entries);
        }
    }
}
