namespace Portal.Security.Authorization;

/// <summary>
/// 권한 평가 규칙 (설계서 6장). 원천 데이터는 <c>FW.USP_AUTH_GET_PERMISSION</c>이 준다.
/// <list type="number">
/// <item>유효 역할은 SP가 계산한다(직접 ∪ 부서 ∪ AD 그룹 ∪ 위임, 유효기간 안).</item>
/// <item>기능 권한 = 유효 역할들의 허용 합집합.</item>
/// <item>사용자 예외: GRANT는 추가, DENY는 역할 허용보다 우선.</item>
/// <item>데이터 범위 = 역할 중 가장 넓은 값. 협력사 사용자는 최대 소속 회사.</item>
/// <item><c>SYS_ADMIN</c>은 모든 기능 허용 (감사 로그 삭제는 DB 권한으로 막는다).</item>
/// </list>
/// </summary>
public static class PermissionEvaluator
{
    /// <summary>시스템 관리자 역할 ID.</summary>
    public const string SysAdminRole = "SYS_ADMIN";

    /// <summary>예외 권한 유형: 추가.</summary>
    public const string Grant = "GRANT";

    /// <summary>예외 권한 유형: 차단.</summary>
    public const string Deny = "DENY";

    /// <summary>원천 데이터로 권한을 평가한다.</summary>
    /// <param name="roles">유효 역할과 역할별 데이터 범위.</param>
    /// <param name="roleFuncs">역할들의 기능 권한.</param>
    /// <param name="userFuncs">사용자 예외 권한.</param>
    /// <param name="isPartner">협력사 사용자 여부 (데이터 범위 상한 적용).</param>
    public static PermissionSet Evaluate(
        IEnumerable<RoleScope> roles,
        IEnumerable<ProgramFunc> roleFuncs,
        IEnumerable<UserProgramFunc> userFuncs,
        bool isPartner)
    {
        var roleList = roles.ToList();
        if (roleList.Count == 0)
        {
            return PermissionSet.Empty;
        }

        var allowed = new HashSet<string>(roleFuncs.Select(f => PermissionSet.Key(f.ProgramId, f.FuncCd)), StringComparer.Ordinal);
        var exceptions = userFuncs.ToList();
        foreach (var f in exceptions.Where(f => f.GrantType == Grant))
        {
            allowed.Add(PermissionSet.Key(f.ProgramId, f.FuncCd));
        }

        foreach (var f in exceptions.Where(f => f.GrantType == Deny))
        {
            allowed.Remove(PermissionSet.Key(f.ProgramId, f.FuncCd));
        }

        var scope = roleList.Select(r => r.DataScopeCd).MinBy(DataScopes.Rank) ?? DataScopes.Self;
        if (isPartner && DataScopes.Rank(scope) < DataScopes.Rank(DataScopes.Company))
        {
            scope = DataScopes.Company;
        }

        var isSysAdmin = roleList.Exists(r => r.RoleId == SysAdminRole);
        return new PermissionSet([.. roleList.Select(r => r.RoleId)], scope, isSysAdmin, allowed);
    }
}

/// <summary>유효 역할 (결과 1).</summary>
public sealed class RoleScope
{
    /// <summary>역할 ID.</summary>
    public string RoleId { get; set; } = "";

    /// <summary>데이터 범위 코드.</summary>
    public string DataScopeCd { get; set; } = DataScopes.Self;
}

/// <summary>역할 기능 권한 (결과 2).</summary>
public sealed class ProgramFunc
{
    /// <summary>프로그램 ID.</summary>
    public string ProgramId { get; set; } = "";

    /// <summary>기능 코드.</summary>
    public string FuncCd { get; set; } = "";
}

/// <summary>사용자 예외 권한 (결과 3).</summary>
public sealed class UserProgramFunc
{
    /// <summary>프로그램 ID.</summary>
    public string ProgramId { get; set; } = "";

    /// <summary>기능 코드.</summary>
    public string FuncCd { get; set; } = "";

    /// <summary><see cref="PermissionEvaluator.Grant"/> 또는 <see cref="PermissionEvaluator.Deny"/>.</summary>
    public string GrantType { get; set; } = "";
}
