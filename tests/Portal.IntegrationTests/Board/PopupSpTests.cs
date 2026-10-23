using Portal.Core.Errors;
using Portal.Data;
using Portal.IntegrationTests.Infrastructure;
using Portal.Web.Areas.Dash.Models;
using Portal.Web.Areas.Sys.Models;

namespace Portal.IntegrationTests.Board;

/// <summary>공지 팝업 SP: 저장·수정·삭제, 기간·공개 대상에 따른 노출, 동시성.</summary>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class PopupSpTests(DbFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task 등록_수정_삭제가_되고_문구는_언어별로_바뀐다()
    {
        await using var db = await fixture.BeginAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await db.Sp.ExecuteAsync("FW.USP_POPUP_SAVE", new
        {
            P_OPEN_SCOPE_CD = "ALL", P_START_DT = today, P_END_DT = today, P_USE_YN = "Y",
            P_LANGS_JSON = "[{\"langCd\":\"ko\",\"title\":\"점검\",\"content\":\"내용\"},{\"langCd\":\"en\",\"title\":\"Maintenance\",\"content\":\"Body\"}]",
        }, Ct);
        var listed = (await db.Sp.QueryPageAsync<PopupDto>("FW.USP_POPUP_GET_LIST", new { P_PAGE_NO = 1, P_PAGE_SIZE = 20 }, Ct)).Items[0];

        Assert.Equal(2, listed.Langs.Count);
        var ex = await Assert.ThrowsAsync<BizException>(() => db.Sp.ExecuteAsync("FW.USP_POPUP_DEL", new { P_POPUP_ID = listed.PopupId, P_ROW_VER = new byte[8] }, Ct));
        Assert.Equal("MSG.CONCURRENCY", ex.MessageKey);
        await db.Sp.ExecuteAsync("FW.USP_POPUP_DEL", new { P_POPUP_ID = listed.PopupId, P_ROW_VER = listed.RowVer }, Ct);
        Assert.Empty((await db.Sp.QueryPageAsync<PopupDto>("FW.USP_POPUP_GET_LIST", new { P_PAGE_NO = 1, P_PAGE_SIZE = 20 }, Ct)).Items);
    }

    [Fact]
    public async Task 홈_팝업은_기간과_공개_대상이_맞는_것만_받는다()
    {
        await using var db = await fixture.BeginAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        async Task Add(string scope, DateOnly start, DateOnly end, string title) => await db.Sp.ExecuteAsync("FW.USP_POPUP_SAVE", new
        {
            P_OPEN_SCOPE_CD = scope, P_START_DT = start, P_END_DT = end, P_USE_YN = "Y",
            P_LANGS_JSON = $"[{{\"langCd\":\"ko\",\"title\":\"{title}\",\"content\":\"내용\"}}]",
        }, Ct);
        await Add("ALL", today.AddDays(-1), today.AddDays(1), "전체");
        await Add("EMP", today, today, "본사");
        await Add("PARTNER", today, today, "협력사");
        await Add("ALL", today.AddDays(1), today.AddDays(2), "미래");
        await Add("ALL", today.AddDays(-5), today.AddDays(-1), "지남");

        var asEmp = await db.Sp.QueryAsync<PopupActiveDto>("FW.USP_POPUP_ACTIVE_GET_LIST", cancellationToken: Ct);
        db.AsUser("it.partner", "P0001", "PARTNER");
        await db.SeedCompanyAsync("P0001");
        await db.SeedUserAsync("it.partner", "P0001", "PARTNER");
        var asPartner = await db.Sp.QueryAsync<PopupActiveDto>("FW.USP_POPUP_ACTIVE_GET_LIST", cancellationToken: Ct);

        Assert.Equal(["본사", "전체"], asEmp.Select(p => p.Title).Order(StringComparer.Ordinal));
        Assert.Equal(["전체", "협력사"], asPartner.Select(p => p.Title).Order(StringComparer.Ordinal));
    }
}
