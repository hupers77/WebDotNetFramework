using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Testing;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.UnitTests.Sys;

/// <summary>사용자 관리 Service: 역할 목록 정리, 빈 역할 ID 거부, 초기화 결과, 화면 프로그램 ID 전달.</summary>
public sealed class UserAdminServiceTests
{
    private readonly FakeSpExecutor _sp = new();

    private UserAdminService Service => new(_sp);

    private static UserSaveRequest Request(params string[] roles) =>
        new() { UserId = "u1", RowVer = [0, 0, 0, 0, 0, 0, 0, 1], UserNm = "  홍길동 ", UserStatCd = "ACTIVE", RoleIds = [.. roles] };

    [Fact]
    public async Task 저장_이름을다듬고_역할은중복없이JSON으로()
    {
        await Service.SaveAsync("SYS_USER_MGT", Request("EMP_USER", " PORTAL_ADMIN", "EMP_USER"));

        var p = _sp.LastCall(UserAdminService.SaveSp).Parameters;
        Assert.Equal(("홍길동", """["EMP_USER","PORTAL_ADMIN"]""", "SYS_USER_MGT"), ((string)p["P_USER_NM"]!, (string)p["P_ROLE_IDS"]!, (string)p["P_PROGRAM_ID"]!));
    }

    [Fact]
    public async Task 저장_부서는다듬고_비우면NULL()
    {
        await Service.SaveAsync("SYS_USER_MGT", new UserSaveRequest { UserId = "u1", RowVer = [0, 0, 0, 0, 0, 0, 0, 1], UserNm = "a", DeptId = " D000001 " });
        var dept = _sp.LastCall(UserAdminService.SaveSp).Parameters["P_DEPT_ID"];
        await Service.SaveAsync("SYS_USER_MGT", new UserSaveRequest { UserId = "u1", RowVer = [0, 0, 0, 0, 0, 0, 0, 1], UserNm = "a", DeptId = "  " });

        Assert.Equal("D000001", dept);
        Assert.Null(_sp.LastCall(UserAdminService.SaveSp).Parameters["P_DEPT_ID"]);
    }

    [Fact]
    public async Task 저장_역할을모두빼면_빈배열()
    {
        await Service.SaveAsync("SYS_USER_MGT", Request());

        Assert.Equal("[]", _sp.LastCall(UserAdminService.SaveSp).Parameters["P_ROLE_IDS"]);
    }

    [Fact]
    public async Task 저장_빈역할ID는_SP를부르지않고거부()
    {
        var ex = await Assert.ThrowsAsync<BizException>(() => Service.SaveAsync("SYS_USER_MGT", Request("EMP_USER", "  ")));

        Assert.Equal(MessageKeys.InvalidInput, ex.MessageKey);
        Assert.Empty(_sp.Calls);
    }

    [Fact]
    public async Task 초기화_바꾼것이없으면false()
    {
        _sp.Returns(UserAdminService.ResetSp, new Portal.Data.SaveResult(0, null));

        Assert.False(await Service.ResetAsync("PTN_USER_MGT", "u1", UserResetActions.Unlock));
        Assert.Equal("PTN_USER_MGT", _sp.LastCall(UserAdminService.ResetSp).Parameters["P_PROGRAM_ID"]);
    }
}
