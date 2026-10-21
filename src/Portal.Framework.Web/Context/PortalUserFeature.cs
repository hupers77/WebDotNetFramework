using Portal.Security.Authentication;
using Portal.Security.Authorization;

namespace Portal.Framework.Web.Context;

/// <summary>인증 핸들러가 확인한 세션 사용자. 요청 하나 동안 <c>HttpContext.Features</c>에 둔다.</summary>
/// <param name="User">세션 사용자.</param>
/// <param name="PasswordChangeRequired">비밀번호 변경 화면 외에는 막아야 하는지.</param>
public sealed record PortalUserFeature(SessionUser User, bool PasswordChangeRequired)
{
    /// <summary>평가된 권한 (권한 필터가 처음 필요할 때 채운다).</summary>
    public PermissionSet? Permissions { get; set; }
}
