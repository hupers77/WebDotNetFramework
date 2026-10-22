using Portal.Core.Context;

namespace Portal.Worker;

/// <summary>
/// Portal.Worker의 SP 공통 파라미터 사용자. 배치 작업은 사람 대신 시스템 계정 <c>SYSTEM</c>으로 기록된다(<c>REG_ID</c>, 감사 로그).
/// </summary>
internal sealed class WorkerCurrentUser : ICurrentUser
{
    /// <summary>배치 작업이 기록에 남기는 사용자 ID.</summary>
    public const string SystemUserId = "SYSTEM";

    /// <inheritdoc />
    public bool IsAuthenticated => true;

    /// <inheritdoc />
    public string? UserId => SystemUserId;

    /// <inheritdoc />
    public string? CompanyCd => "HQ";

    /// <inheritdoc />
    public string? UserTypeCd => "SYSTEM";

    /// <inheritdoc />
    public string LangCd => "ko";

    /// <inheritdoc />
    public string? ClientIp => null;
}
