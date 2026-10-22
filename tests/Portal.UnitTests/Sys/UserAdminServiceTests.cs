using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Localization;
using Portal.Core.Results;
using Portal.Framework.Web.Logging;
using Portal.Testing;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.UnitTests.Sys;

/// <summary>사용자 관리 Service: 역할 목록 정리, 빈 역할 ID 거부, 초기화 결과, 화면 프로그램 ID 전달, 엑셀 내보내기와 개인정보 접속기록.</summary>
public sealed class UserAdminServiceTests
{
    private readonly FakeSpExecutor _sp = new();

    private UserAdminService Service => new(_sp, new PiiAccessLog(_sp), new KeyLocalizer());

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

    [Fact]
    public async Task 내보내기_모든쪽을읽고_접속기록을남긴뒤_파일()
    {
        _sp.Returns(UserAdminService.ListSp, p => Page((int)p["P_PAGE_NO"]! == 1 ? 500 : 1, 501));

        await using var file = await Service.ExportAsync("SYS_USER_MGT", new UserSearch { CompanyCd = "P0001", Keyword = "홍길동" }, "  감사 자료  ");

        var names = _sp.Calls.Select(c => c.SpName).ToList();
        Assert.Equal([UserAdminService.ListSp, UserAdminService.ListSp, PiiAccessLog.WriteSp], names);
        Assert.Equal(500, _sp.Calls[0].Parameters["P_PAGE_SIZE"]);
        var log = _sp.LastCall(PiiAccessLog.WriteSp).Parameters;
        Assert.Equal(("SYS_USER_MGT", PiiActions.Download, "감사 자료"), ((string)log["P_PROGRAM_ID"]!, (string)log["P_ACTION_CD"]!, (string)log["P_REASON"]!));
        Assert.Equal("""{"cnt":501,"companyCd":"P0001","keyword":"Y"}""", log["P_TARGET_DESC"]);
        var sheet = Portal.Infrastructure.Excel.ExcelReader.Read(file, 600);
        Assert.Equal(("LBL.USER_ID", "LBL.USER_NM", 501), (sheet.Headers[0], sheet.Headers[1], sheet.Rows.Count));
    }

    [Fact]
    public async Task 내보내기_최대수를넘으면_기록도파일도없이거부()
    {
        _sp.Returns(UserAdminService.ListSp, Page(500, UserAdminService.MaxExportRows + 1));

        var ex = await Assert.ThrowsAsync<BizException>(() => Service.ExportAsync("SYS_USER_MGT", new UserSearch(), "감사"));

        Assert.Equal(UserAdminMessageKeys.ExportTooMany, ex.MessageKey);
        Assert.DoesNotContain(_sp.Calls, c => c.SpName == PiiAccessLog.WriteSp);
    }

    [Fact]
    public void 접속기록대상_검색어원문은넣지않는다()
    {
        Assert.Equal("""{"cnt":0}""", UserAdminService.TargetDesc(new UserSearch { Keyword = "  " }, 0));
        Assert.DoesNotContain("김철수", UserAdminService.TargetDesc(new UserSearch { Keyword = "김철수", LockYn = "Y" }, 3), StringComparison.Ordinal);
    }

    private static PageResult<UserAdminDto> Page(int count, int total) => new()
    {
        Items = [.. Enumerable.Range(1, count).Select(i => new UserAdminDto { UserId = $"u{i}", UserNm = $"사용자{i}" })],
        TotalCount = total,
        PageNo = 1,
        PageSize = 500,
    };

    /// <summary>번역 대신 키를 그대로 돌려준다.</summary>
    private sealed class KeyLocalizer : IPortalLocalizer
    {
        public string this[string key] => key;

        public string this[string key, params object?[] args] => key;

        public string Get(string langCd, string key, params object?[] args) => key;
    }
}
