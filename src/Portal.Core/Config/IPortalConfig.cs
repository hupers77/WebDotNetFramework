namespace Portal.Core.Config;

/// <summary>
/// 시스템 설정(<c>FW.TB_CONFIG</c>) 읽기. 키는 <see cref="Keys.ConfigKeys"/> 상수로만 넘긴다.
/// 값은 메모리에 캐시되고, 설정 관리 화면에서 바꾸면 캐시 버전(<c>CONFIG</c>) 증가로 몇 초 안에 반영된다.
/// </summary>
public interface IPortalConfig
{
    /// <summary>문자열 값. 키가 없거나 값이 비어 있으면 null.</summary>
    string? GetString(string key);

    /// <summary>정수 값. 키가 없거나 형식이 맞지 않으면 예외(설정 누락은 배포 오류이므로 숨기지 않는다).</summary>
    int GetInt(string key);

    /// <summary>Y/N 값.</summary>
    bool GetBool(string key);
}
