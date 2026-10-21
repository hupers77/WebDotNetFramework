using Portal.Core.Context;
using Portal.Data;
using Portal.Security.Authentication;

namespace Portal.Security.Authorization;

/// <summary>현재 사용자의 권한을 DB에서 읽어 평가한다. 캐시는 호출하는 쪽(웹 계층)이 사용자별로 둔다.</summary>
public sealed class PermissionService(ISpExecutor sp, ICurrentUser currentUser)
{
    /// <summary>협력사 사용자 유형 코드.</summary>
    public const string PartnerUserType = "PARTNER";

    /// <summary>현재 사용자의 권한. 로그인 전이면 <see cref="PermissionSet.Empty"/>.</summary>
    public async Task<PermissionSet> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated)
        {
            return PermissionSet.Empty;
        }

        return await sp.QueryMultipleAsync(
            AuthSp.GetPermission,
            null,
            async reader =>
            {
                var roles = await reader.ReadAsync<RoleScope>(cancellationToken);
                var roleFuncs = await reader.ReadAsync<ProgramFunc>(cancellationToken);
                var userFuncs = await reader.ReadAsync<UserProgramFunc>(cancellationToken);
                return PermissionEvaluator.Evaluate(roles, roleFuncs, userFuncs, currentUser.UserTypeCd == PartnerUserType);
            },
            cancellationToken);
    }
}
