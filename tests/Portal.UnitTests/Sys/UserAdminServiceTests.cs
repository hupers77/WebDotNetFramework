using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Testing;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.UnitTests.Sys;

/// <summary>사용자 관리 Service: 역할 목록 정리, 빈 역할 ID 거부, 초기화 결과.</summary>
public sealed class UserAdminServiceTests
{
    private readonly FakeSpExecutor _sp = new();

    private UserAdminService Service => new(_sp);

    private static UserSaveRequest Request(params string[] roles) =>
        new() { UserId = "u1", RowVer = [0, 0, 0, 0, 0, 0, 0, 1], UserNm = "  홍길동 ", UserStatCd = "ACTIVE", RoleIds = [.. roles] };

    [Fact]
    public async Task 저장_이름을다듬고_역할은중복없이JSON으로()
    {
        await Service.SaveAsync(Request("EMP_USER", " PORTAL_ADMIN", "EMP_USER"));

        var p = _sp.LastCall(UserAdminService.SaveSp).Parameters;
        Assert.Equal(("홍길동", """["EMP_USER","PORTAL_ADMIN"]"""), ((string)p["P_USER_NM"]!, (string)p["P_ROLE_IDS"]!));
    }

    [Fact]
    public async Task 저장_역할을모두빼면_빈배열()
    {
        await Service.SaveAsync(Request());

        Assert.Equal("[]", _sp.LastCall(UserAdminService.SaveSp).Parameters["P_ROLE_IDS"]);
    }

    [Fact]
    public async Task 저장_빈역할ID는_SP를부르지않고거부()
    {
        var ex = await Assert.ThrowsAsync<BizException>(() => Service.SaveAsync(Request("EMP_USER", "  ")));

        Assert.Equal(MessageKeys.InvalidInput, ex.MessageKey);
        Assert.Empty(_sp.Calls);
    }

    [Fact]
    public async Task 초기화_바꾼것이없으면false()
    {
        _sp.Returns(UserAdminService.ResetSp, new Portal.Data.SaveResult(0, null));

        Assert.False(await Service.ResetAsync("u1", UserResetActions.Unlock));
    }
}
