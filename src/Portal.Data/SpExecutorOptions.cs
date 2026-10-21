namespace Portal.Data;

/// <summary>
/// SpExecutor 옵션. 기본값은 초기 데이터 정의서의 <c>SP.COMMAND_TIMEOUT_SEC</c>, <c>LOG.SLOW_SP_MS</c>와 같고,
/// 설정 모듈이 <c>FW.TB_CONFIG</c> 값을 읽어 덮어쓴다.
/// </summary>
public sealed class SpExecutorOptions
{
    /// <summary>설정 섹션 이름.</summary>
    public const string SectionName = "Portal:Data";

    /// <summary>연결 문자열 이름 (<c>ConnectionStrings:Portal</c>).</summary>
    public string ConnectionStringName { get; set; } = "Portal";

    /// <summary>SP 명령 타임아웃(초).</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>이 시간(ms)을 넘긴 SP는 경고 로그를 남긴다.</summary>
    public int SlowThresholdMs { get; set; } = 1000;
}
