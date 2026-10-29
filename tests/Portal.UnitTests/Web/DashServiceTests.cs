using Microsoft.Extensions.Caching.Memory;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Security.Authorization;
using Portal.Testing;
using Portal.Web.Areas.Dash.Models;
using Portal.Web.Areas.Dash.Services;

namespace Portal.UnitTests.Web;

/// <summary>대시보드 위젯: 권한 없는 위젯은 목록에도 데이터에도 없고, 위젯 SP가 아닌 SP는 실행하지 않으며, 바로가기는 사이트 경로만 내보내고, 캐시 시간 동안 SP를 다시 부르지 않는다.</summary>
public sealed class DashServiceTests : IDisposable
{
    private readonly FakeSpExecutor _sp = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 100 });
    private readonly DashService _service;
    private readonly List<WidgetDto> _widgets;

    public DashServiceTests()
    {
        _widgets =
        [
            new() { WidgetCd = "OPEN", TypeCd = "LIST", DataSp = "FW.USP_WIDGET_OPEN", CacheSec = 60 },
            new() { WidgetCd = "BOARD", TypeCd = "LIST", DataSp = "FW.USP_WIDGET_BOARD", ProgramId = "BRD_NOTICE", CacheSec = 0 },
            new() { WidgetCd = "EVIL", TypeCd = "LIST", DataSp = "FW.USP_USER_SAVE" },
            new() { WidgetCd = "BAR", TypeCd = "BAR", DataSp = "FW.USP_WIDGET_BAR" },
            new() { WidgetCd = "CHART", TypeCd = "CHART", DataSp = "FW.USP_WIDGET_CHART" },
        ];
        _sp.Returns(DashService.ListSp, _widgets);
        _sp.Returns("FW.USP_WIDGET_OPEN", new List<WidgetRowDto>
        {
            new() { Text = "안전", Link = "/Board/Notice/View/1" },
            new() { Text = "외부", Link = "https://evil.example/" },
            new() { Text = "상대", Link = "//evil.example/" },
            new() { Text = "스크립트", Link = "javascript:alert(1)" },
        });
        _sp.Returns("FW.USP_WIDGET_BOARD", new List<WidgetRowDto>());
        _service = new DashService(_sp, _cache, new FakeCurrentUser());
    }

    public void Dispose() => _cache.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PermissionSet Perm(params string[] programs) =>
        new(["R"], DataScopes.Self, false, [.. programs.Select(p => PermissionSet.Key(p, FuncCodes.Read))]);

    [Fact]
    public async Task GetWidgetsAsync_읽기권한이_없는_위젯과_모르는_유형은_뺀다()
    {
        var without = await _service.GetWidgetsAsync(Perm(), Ct);
        var with = await _service.GetWidgetsAsync(Perm("BRD_NOTICE"), Ct);

        Assert.Equal(["OPEN", "EVIL", "CHART"], without.Select(w => w.WidgetCd));
        Assert.Equal(["OPEN", "BOARD", "EVIL", "CHART"], with.Select(w => w.WidgetCd));
    }

    [Fact]
    public async Task GetDataAsync_권한없는_위젯은_없는_것과_같다()
    {
        var ex = await Assert.ThrowsAsync<BizException>(() => _service.GetDataAsync("BOARD", Perm(), Ct));

        Assert.Equal(MessageKeys.NotFound, ex.MessageKey);
        Assert.DoesNotContain(_sp.Calls, c => c.SpName == "FW.USP_WIDGET_BOARD");
    }

    [Fact]
    public async Task GetDataAsync_위젯SP가_아니면_실행하지_않는다()
    {
        await Assert.ThrowsAsync<BizException>(() => _service.GetDataAsync("EVIL", Perm(), Ct));

        Assert.DoesNotContain(_sp.Calls, c => c.SpName == "FW.USP_USER_SAVE");
    }

    [Fact]
    public async Task GetDataAsync_바로가기는_사이트_안_경로만_남긴다()
    {
        var rows = await _service.GetDataAsync("OPEN", Perm(), Ct);

        Assert.Equal(["/Board/Notice/View/1", null, null, null], rows.Select(r => r.Link));
    }

    [Fact]
    public async Task GetDataAsync_캐시시간_안에는_SP를_다시_부르지_않고_0이면_매번_부른다()
    {
        var perm = Perm("BRD_NOTICE");

        await _service.GetDataAsync("OPEN", perm, Ct);
        await _service.GetDataAsync("OPEN", perm, Ct);
        await _service.GetDataAsync("BOARD", perm, Ct);
        await _service.GetDataAsync("BOARD", perm, Ct);

        Assert.Equal(1, _sp.Calls.Count(c => c.SpName == "FW.USP_WIDGET_OPEN"));
        Assert.Equal(2, _sp.Calls.Count(c => c.SpName == "FW.USP_WIDGET_BOARD"));
    }
}
