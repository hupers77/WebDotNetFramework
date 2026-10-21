namespace Portal.Core.Config;

/// <summary>설정 키가 없거나 값 형식이 맞지 않을 때. 초기 데이터(<c>db/seed/FW/05_CONFIG.sql</c>)를 확인한다.</summary>
public sealed class ConfigValueException(string key, string reason)
    : Exception($"설정 '{key}': {reason}")
{
    /// <summary>문제가 된 설정 키.</summary>
    public string Key { get; } = key;
}
