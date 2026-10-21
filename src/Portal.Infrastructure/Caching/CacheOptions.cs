namespace Portal.Infrastructure.Caching;

/// <summary>캐시 설정 (<c>appsettings.json</c>의 <c>Portal:Cache</c>). 서버마다 다를 수 있는 값이라 DB 설정이 아닌 파일에 둔다.</summary>
public sealed class CacheOptions
{
    /// <summary>설정 섹션 이름.</summary>
    public const string SectionName = "Portal:Cache";

    /// <summary>캐시 버전 확인 주기(초).</summary>
    public int PollSeconds { get; set; } = 5;
}
