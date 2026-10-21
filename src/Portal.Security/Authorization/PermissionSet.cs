using System.Collections.Frozen;

namespace Portal.Security.Authorization;

/// <summary>
/// 한 사용자의 평가된 권한. 로그인 시 계산해 사용자별로 캐시하고, 권한 데이터가 바뀌면(캐시 버전 <c>AUTH</c>) 다시 만든다.
/// 서버 필터(<c>[Func]</c>)와 화면 버튼 표시가 모두 이 객체로 판단하므로 둘이 항상 일치한다.
/// </summary>
public sealed class PermissionSet
{
    private readonly FrozenSet<string> allowed;

    internal PermissionSet(IReadOnlyCollection<string> roles, string dataScopeCd, bool isSysAdmin, IEnumerable<string> allowed)
    {
        Roles = roles;
        DataScopeCd = dataScopeCd;
        IsSysAdmin = isSysAdmin;
        this.allowed = allowed.ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>권한이 하나도 없는 사용자 (로그인 전, 역할 없음).</summary>
    public static PermissionSet Empty { get; } = new([], DataScopes.Self, false, []);

    /// <summary>유효 역할 ID.</summary>
    public IReadOnlyCollection<string> Roles { get; }

    /// <summary>역할들 중 가장 넓은 데이터 범위 (<see cref="DataScopes"/>). 실제 조회 제한은 SP가 한다.</summary>
    public string DataScopeCd { get; }

    /// <summary><c>SYS_ADMIN</c> 역할 보유. 모든 기능이 허용된다.</summary>
    public bool IsSysAdmin { get; }

    /// <summary>프로그램의 기능을 쓸 수 있는지.</summary>
    public bool Can(string programId, string funcCd) =>
        IsSysAdmin || allowed.Contains(Key(programId, funcCd));

    /// <summary>프로그램에 허용된 기능 코드 목록 (화면 JS의 <c>fw.auth.can</c>에 내려준다).</summary>
    public IReadOnlyList<string> FuncsOf(string programId)
    {
        var prefix = programId + "|";
        return [.. allowed.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Select(k => k[prefix.Length..]).Order(StringComparer.Ordinal)];
    }

    internal static string Key(string programId, string funcCd) => programId + "|" + funcCd;
}

/// <summary>데이터 범위 코드 (<c>TB_ROLE.DATA_SCOPE_CD</c>). 넓은 것부터.</summary>
public static class DataScopes
{
    /// <summary>전체.</summary>
    public const string All = "ALL";

    /// <summary>소속 회사.</summary>
    public const string Company = "COMPANY";

    /// <summary>소속 부서와 하위 부서.</summary>
    public const string DeptSub = "DEPT_SUB";

    /// <summary>소속 부서.</summary>
    public const string Dept = "DEPT";

    /// <summary>본인.</summary>
    public const string Self = "SELF";

    /// <summary>넓은 순서 (작을수록 넓다). 모르는 코드는 가장 좁게 본다.</summary>
    internal static int Rank(string scope) => scope switch
    {
        All => 0,
        Company => 1,
        DeptSub => 2,
        Dept => 3,
        _ => 4,
    };
}
