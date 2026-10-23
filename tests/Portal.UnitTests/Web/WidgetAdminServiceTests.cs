using System.Text.Json;
using Portal.Core.Errors;
using Portal.Data;
using Portal.Testing;
using Portal.Web.Areas.Dash.Models;
using Portal.Web.Areas.Dash.Services;

namespace Portal.UnitTests.Web;

/// <summary>위젯 관리 Service의 입력 규칙: 수정 행만, 동시성 값 필수, Y/N만 허용하고 SP에는 JSON으로 넘긴다.</summary>
public sealed class WidgetAdminServiceTests
{
    private static readonly byte[] RowVer = [0, 0, 0, 0, 0, 0, 0, 0x2B];
    private readonly FakeSpExecutor _sp = new();
    private readonly WidgetAdminService _service;

    public WidgetAdminServiceTests()
    {
        _sp.Returns(WidgetAdminService.SaveSp, new SaveResult(1, null));
        _service = new WidgetAdminService(_sp);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static WidgetAdminSaveRow Row(string code = "NOTICE_RECENT", string rowStat = "U", byte[]? rowVer = null) => new()
    {
        RowStat = rowStat, WidgetCd = code, NameKo = " 공지 ", NameEn = "Notices", NameZh = "公告", SpanCols = 8, CacheSec = 60, RowVer = rowVer ?? RowVer,
    };

    private static WidgetAdminSaveRequest Request(params WidgetAdminSaveRow[] rows) => new() { Rows = [.. rows] };

    [Fact]
    public async Task SaveAsync_제목은_공백을_빼고_RowVer는_16진수_JSON으로_넘긴다()
    {
        var result = await _service.SaveAsync(Request(Row()), Ct);

        var json = JsonDocument.Parse((string)_sp.LastCall(WidgetAdminService.SaveSp).Parameters["P_ROWS_JSON"]!).RootElement[0];
        Assert.Equal(1, result.AffectedCnt);
        Assert.Equal("공지", json.GetProperty("nameKo").GetString());
        Assert.Equal(8, json.GetProperty("spanCols").GetInt32());
        Assert.Equal("000000000000002B", json.GetProperty("rowVer").GetString());
    }

    [Fact]
    public async Task SaveAsync_빈_요청은_변경없음()
    {
        var ex = await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(), Ct));

        Assert.Equal("MSG.NO_CHANGES", ex.MessageKey);
    }

    [Theory]
    [InlineData("I")]
    [InlineData("D")]
    public async Task SaveAsync_수정_행만_허용(string rowStat)
    {
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(Row(rowStat: rowStat)), Ct));

        Assert.Empty(_sp.Calls);
    }

    [Fact]
    public async Task SaveAsync_RowVer가_없거나_같은_위젯이_두번이거나_사용여부가_이상하면_거절()
    {
        var bad = Row();
        bad.UseYn = "X";

        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(Row(rowVer: [1])), Ct));
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(Row(), Row()), Ct));
        await Assert.ThrowsAsync<BizException>(() => _service.SaveAsync(Request(bad), Ct));

        Assert.Empty(_sp.Calls);
    }
}
