namespace Portal.Security.Authentication;

/// <summary>
/// 외부 시스템(AD 등)으로 비밀번호를 확인하는 확장 지점. 업무 저장소가 구현해 DI에 등록하면
/// <see cref="AuthService.LoginAsync"/>가 이 검증기가 맡는 사용자에 한해 포털 비밀번호 해시 대신 검증기로 확인한다.
/// 등록하지 않으면 지금과 완전히 같게 동작한다. 검증 성공 뒤(로그인 기록, 실패 횟수·잠금, 2단계 인증, 세션 발급)는 프레임워크 규칙 그대로다.
/// </summary>
public interface IExternalCredentialValidator
{
    /// <summary>이 검증기가 확인할 사용자인지 (예: <c>TB_USER_LOGIN_MAP</c>에 <c>PROVIDER_CD = 'AD'</c> 행이 있는 사용자).</summary>
    Task<bool> CanHandleAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// 입력한 비밀번호를 외부 시스템으로 확인한다. 비밀번호는 저장·로그하지 않는다.
    /// 서버에 연결하지 못했으면 <see cref="ExternalCredentialResult.Unavailable"/> 결과(또는 예외)로 알려, 틀린 비밀번호와 구분한다.
    /// </summary>
    Task<ExternalCredentialResult> ValidateAsync(string userId, string password, CancellationToken cancellationToken);
}

/// <summary>외부 비밀번호 확인 결과.</summary>
/// <param name="Success">비밀번호가 맞는지.</param>
/// <param name="FailureCode">실패 사유 코드 (선택). <see cref="Unavailable"/>이면 실패 횟수에 넣지 않는다.</param>
public sealed record ExternalCredentialResult(bool Success, string? FailureCode = null)
{
    /// <summary>외부 서버 연결 실패·시간 초과 (비밀번호 오류가 아님).</summary>
    public const string Unavailable = "UNAVAILABLE";
}
