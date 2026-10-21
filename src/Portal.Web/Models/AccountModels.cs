namespace Portal.Web.Models;

/// <summary>로그인 화면 입력.</summary>
public sealed class LoginForm
{
    /// <summary>사용자 ID.</summary>
    public string? UserId { get; set; }

    /// <summary>비밀번호. 화면에 다시 채우지 않는다.</summary>
    public string? Password { get; set; }

    /// <summary>로그인 후 돌아갈 같은 사이트 경로.</summary>
    public string? ReturnUrl { get; set; }
}

/// <summary>로그인 화면 표시 정보.</summary>
/// <param name="UserId">입력했던 사용자 ID.</param>
/// <param name="ReturnUrl">로그인 후 돌아갈 경로.</param>
/// <param name="MessageKey">안내·오류 메시지 키.</param>
public sealed record LoginViewModel(string? UserId, string? ReturnUrl, string? MessageKey);

/// <summary>비밀번호 변경 입력.</summary>
public sealed class PasswordForm
{
    /// <summary>현재 비밀번호.</summary>
    public string? CurrentPassword { get; set; }

    /// <summary>새 비밀번호.</summary>
    public string? NewPassword { get; set; }

    /// <summary>새 비밀번호 확인.</summary>
    public string? NewPasswordConfirm { get; set; }
}

/// <summary>비밀번호 변경 화면 표시 정보.</summary>
/// <param name="MinLength3Types">3종류 조합 최소 길이.</param>
/// <param name="MinLength2Types">2종류 조합 최소 길이.</param>
/// <param name="Required">변경해야 다른 화면을 쓸 수 있는지.</param>
/// <param name="ErrorMessage">번역된 오류 메시지.</param>
public sealed record PasswordViewModel(int MinLength3Types, int MinLength2Types, bool Required, string? ErrorMessage);

/// <summary>오류 화면 표시 정보.</summary>
/// <param name="StatusCode">HTTP 상태 코드.</param>
/// <param name="TitleKey">제목 다국어 키.</param>
/// <param name="MessageKey">본문 다국어 키.</param>
/// <param name="TraceId">추적 ID (관리자 문의용).</param>
public sealed record ErrorViewModel(int StatusCode, string TitleKey, string MessageKey, string TraceId);
