using Portal.Data;
using Portal.IntegrationTests.Infrastructure;
using Portal.Web.Areas.Dash.Models;
using Portal.Web.Areas.Dash.Services;

namespace Portal.IntegrationTests.Board;

/// <summary>위젯 관리 SP: 설정·제목 저장, 동시성 충돌, 위젯 SP 규칙 체크 제약.</summary>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class WidgetAdminSpTests(DbFixture fixture)
{
    [Fact]
    public async Task 저장하면_칸수와_제목이_바뀌고_이전_RowVer로_다시_저장하면_충돌()
    {
        await using var db = await fixture.BeginAsync();
        var service = new WidgetAdminService(db.Sp);
        var widget = (await service.GetListAsync(TestContext.Current.CancellationToken)).Items.Single(w => w.WidgetCd == "NOTICE_RECENT");
        WidgetAdminSaveRow Row(Action<WidgetAdminSaveRow> change)
        {
            var row = new WidgetAdminSaveRow
            {
                RowStat = "U", WidgetCd = widget.WidgetCd, NameKo = widget.NameKo!, NameEn = widget.NameEn!, NameZh = widget.NameZh!,
                SpanCols = widget.SpanCols, CacheSec = widget.CacheSec, SortNo = widget.SortNo, UseYn = widget.UseYn, RowVer = widget.RowVer,
            };
            change(row);
            return row;
        }

        await service.SaveAsync(new WidgetAdminSaveRequest { Rows = [Row(r => { r.SpanCols = 12; r.NameEn = "News"; })] }, TestContext.Current.CancellationToken);
        var after = (await service.GetListAsync(TestContext.Current.CancellationToken)).Items.Single(w => w.WidgetCd == "NOTICE_RECENT");

        Assert.Equal((12, "News"), (after.SpanCols, after.NameEn));
        await Assert.ThrowsAsync<Portal.Core.Errors.BizException>(() =>
            service.SaveAsync(new WidgetAdminSaveRequest { Rows = [Row(r => r.CacheSec = 5)] }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task 위젯_데이터_SP는_USP_WIDGET_규칙이_아니면_등록할_수_없다()
    {
        await using var db = await fixture.BeginAsync();

        var ex = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.ExecAsync(
            "INSERT INTO FW.TB_WIDGET (WIDGET_CD, TYPE_CD, DATA_SP) VALUES ('BAD', 'LIST', 'FW.USP_USER_SAVE')"));

        Assert.Equal(547, ex.Number);
    }
}
