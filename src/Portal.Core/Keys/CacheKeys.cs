namespace Portal.Core.Keys;

/// <summary><c>FW.TB_CACHE_VERSION</c>의 캐시 키. 값을 바꾸는 SP는 해당 키의 버전을 올린다.</summary>
public static class CacheKeys
{
    /// <summary>메뉴 트리, 프로그램·API 매핑.</summary>
    public const string Menu = "MENU";

    /// <summary>사용자별 권한 집합.</summary>
    public const string Auth = "AUTH";

    /// <summary>공통코드.</summary>
    public const string Code = "CODE";

    /// <summary>다국어 리소스.</summary>
    public const string Lang = "LANG";

    /// <summary>시스템 설정.</summary>
    public const string Config = "CONFIG";
}
