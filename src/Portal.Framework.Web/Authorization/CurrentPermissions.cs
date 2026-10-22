using Microsoft.AspNetCore.Http;
using Portal.Framework.Web.Context;
using Portal.Security.Authorization;

namespace Portal.Framework.Web.Authorization;

/// <summary>현재 요청 사용자의 권한. 사용자별 캐시를 거치고 요청 안에서는 한 번만 구한다(권한 필터, 메뉴, 버튼이 같은 값을 쓴다).</summary>
public sealed class CurrentPermissions(IHttpContextAccessor accessor, PermissionService permissions, PermissionCache cache)
{
    /// <summary>권한. 로그인 전이면 빈 권한.</summary>
    public async Task<PermissionSet> GetAsync(CancellationToken cancellationToken = default)
    {
        if (accessor.HttpContext?.Features.Get<PortalUserFeature>() is not { } user)
        {
            return PermissionSet.Empty;
        }

        return user.Permissions ??= await cache.GetOrLoadAsync(user.User.UserId, () => permissions.GetCurrentAsync(cancellationToken));
    }
}
