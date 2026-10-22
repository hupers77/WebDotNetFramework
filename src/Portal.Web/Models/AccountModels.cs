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

/// <summary>즐겨찾기 변경 요청.</summary>
/// <param name="MenuId">메뉴 ID.</param>
/// <param name="Favorite">true면 추가, false면 해제.</param>
public sealed record FavoriteRequest(int MenuId, bool Favorite);

/// <summary>협력사 가입 신청 입력.</summary>
public sealed class JoinForm
{
    /// <summary>소속 회사 사업자등록번호.</summary>
    public string? BizRegNo { get; set; }

    /// <summary>희망 사용자 ID.</summary>
    public string? UserId { get; set; }

    /// <summary>이름.</summary>
    public string? UserNm { get; set; }

    /// <summary>업무용 이메일.</summary>
    public string? Email { get; set; }

    /// <summary>비밀번호. 화면에 다시 채우지 않는다.</summary>
    public string? Password { get; set; }

    /// <summary>비밀번호 확인.</summary>
    public string? PasswordConfirm { get; set; }
}

/// <summary>협력사 가입 신청 화면 표시 정보.</summary>
/// <param name="Form">입력했던 값 (비밀번호 제외).</param>
/// <param name="MinLength3Types">3종류 조합 최소 길이.</param>
/// <param name="MinLength2Types">2종류 조합 최소 길이.</param>
/// <param name="ErrorMessage">번역된 오류 메시지.</param>
/// <param name="Done">신청을 마쳤는지 (완료 안내만 보인다).</param>
public sealed record JoinViewModel(JoinForm Form, int MinLength3Types, int MinLength2Types, string? ErrorMessage, bool Done = false);

/// <summary>2단계 인증 코드 입력.</summary>
public sealed class MfaForm
{
    /// <summary>인증 앱에 보이는 6자리 코드.</summary>
    public string? Code { get; set; }

    /// <summary>로그인 후 돌아갈 같은 사이트 경로.</summary>
    public string? ReturnUrl { get; set; }
}

/// <summary>2단계 인증 화면 표시 정보.</summary>
/// <param name="ReturnUrl">로그인 후 돌아갈 경로.</param>
/// <param name="MessageKey">오류 메시지 키.</param>
/// <param name="Enroll">처음 등록이면 등록 키와 QR. 이미 등록했으면 null.</param>
public sealed record MfaViewModel(string? ReturnUrl, string? MessageKey, MfaEnrollInfo? Enroll);

/// <summary>인증 앱 등록 정보.</summary>
/// <param name="SecretKey">직접 입력할 등록 키 (Base32, 4자씩 띄움).</param>
/// <param name="KeyUri">휴대폰에서 바로 여는 <c>otpauth://</c> 링크.</param>
/// <param name="QrSvg">QR 코드 SVG 마크업 (서버가 만든 값).</param>
public sealed record MfaEnrollInfo(string SecretKey, string KeyUri, Microsoft.AspNetCore.Html.IHtmlContent QrSvg);
