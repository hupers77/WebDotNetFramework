using Portal.Testing;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;
using Portal.Web.Models;
using Portal.Web.Services;

namespace Portal.UnitTests.Web;

/// <summary>내 정보·운영 모니터링 Service: SP 결과셋을 화면 모델로 묶는다 (대상 사용자는 파라미터가 아니라 현재 로그인 사용자).</summary>
public sealed class ProfileAndMonitorServiceTests
{
    private readonly FakeSpExecutor _sp = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Profile_계정과_역할을_돌려주고_사용자_ID_파라미터를_받지_않는다()
    {
        _sp.Returns(ProfileService.GetSp, new object[]
        {
            new List<ProfileDto> { new() { UserId = "u1", UserNm = "홍길동" } },
            new List<ProfileRoleDto> { new() { RoleId = "EMP_USER", RoleNm = "임직원" } },
        });

        var found = await new ProfileService(_sp).GetAsync(Ct);

        Assert.Equal("u1", found!.Value.Profile.UserId);
        Assert.Single(found.Value.Roles);
        Assert.Empty(_sp.LastCall(ProfileService.GetSp).Parameters);
    }

    [Fact]
    public async Task Profile_계정이_없으면_null()
    {
        _sp.Returns(ProfileService.GetSp, new object[] { new List<ProfileDto>(), new List<ProfileRoleDto>() });

        Assert.Null(await new ProfileService(_sp).GetAsync(Ct));
    }

    [Fact]
    public async Task Monitor_DB_현황과_서버_정보를_합친다()
    {
        _sp.Returns(MonitorService.GetSp, new object[]
        {
            new MonitorSummary { SessionCnt = 3, ErrorCnt = 1 },
            new List<MonitorCache> { new() { CacheKey = "MENU", VersionNo = 4 } },
            new List<MonitorJob> { new() { JobId = "JOB_A", OverdueYn = "Y" } },
            new List<MonitorStatus> { new() { StatCd = "READY", Cnt = 2 } },
            new List<MonitorStatus>(),
            new List<MonitorError> { new() { Message = "오류" } },
        });

        var result = await new MonitorService(_sp).GetAsync(Ct);

        Assert.Equal(3, result.Summary.SessionCnt);
        Assert.Equal("Y", result.Jobs[0].OverdueYn);
        Assert.Equal(2, result.Mails[0].Cnt);
        Assert.Single(result.Errors);
        Assert.False(string.IsNullOrEmpty(result.Server.Version));
        Assert.True(result.Server.WorkingSetMb > 0);
    }
}
