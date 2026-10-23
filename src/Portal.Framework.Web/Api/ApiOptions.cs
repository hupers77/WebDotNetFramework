namespace Portal.Framework.Web.Api;

/// <summary>외부 API 설정 (<c>appsettings</c>의 <c>Api</c> 구역).</summary>
public sealed class ApiOptions
{
    /// <summary>구역 이름.</summary>
    public const string SectionName = "Api";

    /// <summary>
    /// 토큰 서명 키 (Base64, 32바이트 이상). 운영에서는 비밀 저장소·환경 변수로만 주입하고 저장소에 올리지 않는다.
    /// 비어 있으면 개발·테스트 환경에서만 실행 때마다 임의 키를 만들어 쓴다(재시작하면 발급한 토큰이 무효가 된다).
    /// </summary>
    public string? SigningKey { get; set; }

    /// <summary>토큰 발급자(iss).</summary>
    public string Issuer { get; set; } = "portal";

    /// <summary>토큰 대상(aud).</summary>
    public string Audience { get; set; } = "portal-api";
}
