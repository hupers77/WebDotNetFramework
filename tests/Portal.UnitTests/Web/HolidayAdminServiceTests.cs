using System.Text.Json;
using Portal.Core.Errors;
using Portal.Data;
using Portal.Testing;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.UnitTests.Web;

/// <summary>휴일 Service의 입력 규칙: 날짜 형식·연도 범위, 행 상태별 필수 값, 같은 날짜 중복을 SP 호출 전에 거절하고 SP에는 JSON으로 넘긴다.</summary>
public sealed class HolidayAdminServiceTests
{
    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 0x3C];
    private readonly FakeSpExecutor _sp = new();
    private readonly HolidayAdminService _service;

    public HolidayAdminServiceTests()
    {
        _sp.Returns(HolidayAdminService.SaveSp, new SaveResult(2, null));
        _service = new HolidayAdminService(_sp);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HolidaySaveRow Row(string rowStat = "I", string date = "2026-05-05", string? name = " 어린이날 ", byte[]? rowVer = null, string workYn = "N") =>
        new() { RowStat = rowStat, HolidayDt = date, HolidayNm = name, WorkYn = workYn, RowVer = rowVer };

    private static HolidaySaveRequest Request(params HolidaySaveRow[] rows) => new() { Rows = [.. rows] };

    [Fact]
    public async Task SaveAsync_추가는_이름_공백을_빼고_수정은_RowVer를_16진수로_넘긴다()
    {
        var result = await _service.SaveAsync(Request(Row(), Row("U", "2026-05-06", "대체", RowVer, "Y")), Ct);

        var json = JsonDocument.Parse((string)_sp.LastCall(HolidayAdminService.SaveSp).Parameters["P_ROWS_JSON"]!).RootElement;
        Assert.Equal(2, result.AffectedCnt);
        Assert.Equal("어린이날", json[0].GetProperty("holidayNm").GetString());
        Assert.Equal(JsonValueKind.Null, json[0].GetProperty("rowVer").ValueKind);
        Assert.Equal("000000000000003C", json[1].GetProperty("rowVer").GetString());
        Assert.Equal("Y", json[1].GetProperty("workYn").GetString());
    }

    [Fact]
    public async Task SaveAsync_삭제는_이름없이_RowVer만_있으면_된다()
    {
        await _service.SaveAsync(Request(Row("D", name: null, rowVer: RowVer)), Ct);

        Assert.Single(_sp.Calls, c => c.SpName == HolidayAdminService.SaveSp);
    }

    [Fact]
    public async Task SaveAsync_빈_요청은_변경없음()
    {
        var ex = await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(), Ct));

        Assert.Equal("MSG.NO_CHANGES", ex.MessageKey);
    }

    [Theory]
    [InlineData("2026-13-01")]
    [InlineData("2026-5-5")]
    [InlineData("20260505")]
    [InlineData("1989-12-31")]
    [InlineData("2101-01-01")]
    [InlineData("")]
    public async Task SaveAsync_날짜_형식과_범위가_틀리면_거절(string date)
    {
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(Row(date: date)), Ct));

        Assert.Empty(_sp.Calls);
    }

    [Fact]
    public async Task SaveAsync_필수값이_빠지거나_같은_날짜가_두번이거나_행상태가_이상하면_거절()
    {
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(Row(name: " ")), Ct));
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(Row("U")), Ct));
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(Row("D")), Ct));
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(Row("X")), Ct));
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(Row(workYn: "?")), Ct));
        var dup = await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(Row(), Row(name: "다른")), Ct));

        Assert.Equal("VAL.DUP_ROW", dup.MessageKey);
        Assert.Empty(_sp.Calls);
    }
}
