using System.Globalization;
using Portal.Core.Config;

namespace Portal.Testing;

/// <summary>
/// <see cref="IPortalConfig"/> 대역. 기본값은 초기 데이터(<c>db/seed/FW/05_CONFIG.sql</c>)와 같게 두고, 테스트에서 필요한 키만 바꾼다.
/// </summary>
public sealed class FakePortalConfig : IPortalConfig
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal)
    {
        ["PWD.MIN_LEN_3TYPES"] = "8",
        ["PWD.MIN_LEN_2TYPES"] = "10",
        ["PWD.HISTORY_COUNT"] = "3",
        ["PWD.EXPIRE_DAYS"] = "90",
        ["PWD.LOCK_COUNT"] = "5",
        ["PWD.LOCK_MINUTES"] = "30",
        ["SESSION.IDLE_MINUTES"] = "30",
        ["SESSION.CONCURRENT_POLICY"] = "KICK_OLD",
        ["UI.TAB_MODE"] = "Y",
        ["UI.MAX_TABS"] = "10",
        ["SITE.THEME_COLOR"] = "#1F5FBF",
        ["FILE.MAX_SIZE_MB"] = "20",
        ["FILE.MAX_COUNT"] = "10",
        ["AUTH.PARTNER_MFA"] = "Y",
        ["AUTH.ADMIN_MFA"] = "Y",
        ["MAIL.ENABLED"] = "N",
        ["MAIL.SMTP_PORT"] = "587",
        ["MAIL.SMTP_SSL"] = "Y",
        ["NOTIFY.POLL_SECONDS"] = "60",
        ["NOTIFY.KEEP_DAYS"] = "90",
    };

    /// <summary>값을 바꾼다.</summary>
    public FakePortalConfig Set(string key, string? value)
    {
        _values[key] = value;
        return this;
    }

    public string? GetString(string key) => _values.GetValueOrDefault(key);

    public int GetInt(string key) =>
        int.TryParse(GetString(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : throw new ConfigValueException(key, "정수 값이 없습니다");

    public bool GetBool(string key) => GetString(key) switch
    {
        "Y" => true,
        "N" => false,
        _ => throw new ConfigValueException(key, "Y/N 값이 없습니다"),
    };
}
