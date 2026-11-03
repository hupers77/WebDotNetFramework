using Portal.Infrastructure.Localization;

namespace Portal.Web.Models;

/// <summary>내 정보 (<c>FW.USP_MY_PROFILE_GET</c>). 이메일·비밀번호 정보는 담지 않는다.</summary>
public sealed class ProfileDto
{
    /// <summary>사용자 ID.</summary>
    public string UserId { get; set; } = "";

    /// <summary>이름.</summary>
    public string UserNm { get; set; } = "";

    /// <summary>회사 코드.</summary>
    public string CompanyCd { get; set; } = "";

    /// <summary>회사명.</summary>
    public string CompanyNm { get; set; } = "";

    /// <summary>부서 ID.</summary>
    public string? DeptId { get; set; }

    /// <summary>부서명.</summary>
    public string? DeptNm { get; set; }

    /// <summary>사용자 유형 (EMP/PARTNER/SYSTEM).</summary>
    public string UserTypeCd { get; set; } = "";

    /// <summary>화면 언어.</summary>
    public string LangCd { get; set; } = "";

    /// <summary>마지막 로그인 (UTC).</summary>
    public DateTime? LastLoginDt { get; set; }

    /// <summary>비밀번호를 마지막으로 바꾼 일시 (UTC).</summary>
    public DateTime? PwdChgDt { get; set; }

    /// <summary>계정 사용 기한 (협력사).</summary>
    public DateTime? AccountEndDt { get; set; }

    /// <summary>2단계 인증 등록 여부.</summary>
    public string MfaYn { get; set; } = "N";
}

/// <summary>내가 가진 역할.</summary>
public sealed class ProfileRoleDto
{
    /// <summary>역할 ID.</summary>
    public string RoleId { get; set; } = "";

    /// <summary>역할명.</summary>
    public string RoleNm { get; set; } = "";
}

/// <summary>내 정보 화면 표시 정보.</summary>
/// <param name="Profile">계정 정보.</param>
/// <param name="Roles">역할.</param>
/// <param name="Languages">고를 수 있는 언어.</param>
public sealed record ProfileViewModel(ProfileDto Profile, IReadOnlyList<ProfileRoleDto> Roles, IReadOnlyList<LanguageInfo> Languages);

/// <summary>내 정보 변경 입력 (화면 언어).</summary>
public sealed class ProfileForm
{
    /// <summary>새 화면 언어.</summary>
    public string? Lang { get; set; }
}
