using Portal.Security.Authorization;

namespace Portal.UnitTests.Security;

/// <summary>권한 평가 규칙 (설계서 6장).</summary>
public sealed class PermissionEvaluatorTests
{
    private static RoleScope Role(string id, string scope = DataScopes.Self) => new() { RoleId = id, DataScopeCd = scope };

    private static ProgramFunc F(string program, string func) => new() { ProgramId = program, FuncCd = func };

    private static UserProgramFunc U(string program, string func, string type) => new() { ProgramId = program, FuncCd = func, GrantType = type };

    [Fact]
    public void 역할기능권한_합집합()
    {
        var set = PermissionEvaluator.Evaluate(
            [Role("A"), Role("B")],
            [F("P1", "READ"), F("P1", "UPDATE"), F("P2", "READ")],
            [],
            isPartner: false);

        Assert.True(set.Can("P1", "UPDATE"));
        Assert.True(set.Can("P2", "READ"));
        Assert.False(set.Can("P2", "UPDATE"));
        Assert.Equal(["READ", "UPDATE"], set.FuncsOf("P1"));
    }

    [Fact]
    public void 사용자GRANT_역할에없는기능추가()
    {
        var set = PermissionEvaluator.Evaluate([Role("A")], [F("P1", "READ")], [U("P1", "EXPORT", PermissionEvaluator.Grant)], false);

        Assert.True(set.Can("P1", "EXPORT"));
    }

    [Fact]
    public void 사용자DENY_역할허용보다우선_GRANT와겹쳐도DENY()
    {
        var set = PermissionEvaluator.Evaluate(
            [Role("A")],
            [F("P1", "READ"), F("P1", "DELETE")],
            [U("P1", "DELETE", PermissionEvaluator.Deny), U("P1", "UPDATE", PermissionEvaluator.Grant), U("P1", "UPDATE", PermissionEvaluator.Deny)],
            false);

        Assert.True(set.Can("P1", "READ"));
        Assert.False(set.Can("P1", "DELETE"));
        Assert.False(set.Can("P1", "UPDATE"));
    }

    [Fact]
    public void 역할없음_예외GRANT가있어도권한없음()
    {
        var set = PermissionEvaluator.Evaluate([], [], [U("P1", "READ", PermissionEvaluator.Grant)], false);

        Assert.Same(PermissionSet.Empty, set);
        Assert.False(set.Can("P1", "READ"));
    }

    [Fact]
    public void SYS_ADMIN_모든기능허용()
    {
        var set = PermissionEvaluator.Evaluate([Role(PermissionEvaluator.SysAdminRole, DataScopes.All)], [], [], false);

        Assert.True(set.IsSysAdmin);
        Assert.True(set.Can("ANY_PROGRAM", "DELETE"));
    }

    [Fact]
    public void 데이터범위_가장넓은값()
    {
        var set = PermissionEvaluator.Evaluate([Role("A", DataScopes.Dept), Role("B", DataScopes.Company), Role("C", DataScopes.Self)], [], [], false);

        Assert.Equal(DataScopes.Company, set.DataScopeCd);
    }

    [Fact]
    public void 협력사사용자_데이터범위상한은소속회사()
    {
        var set = PermissionEvaluator.Evaluate([Role("A", DataScopes.All)], [], [], isPartner: true);

        Assert.Equal(DataScopes.Company, set.DataScopeCd);
    }

    [Fact]
    public void 모르는범위코드_가장좁게()
    {
        var set = PermissionEvaluator.Evaluate([Role("A", "WHATEVER"), Role("B", DataScopes.Dept)], [], [], false);

        Assert.Equal(DataScopes.Dept, set.DataScopeCd);
    }

    [Fact]
    public void 프로그램ID와기능코드_구분자로섞이지않는다()
    {
        var set = PermissionEvaluator.Evaluate([Role("A")], [F("P1", "READ")], [], false);

        Assert.False(set.Can("P", "1|READ"));
        Assert.Empty(set.FuncsOf("P"));
    }
}
