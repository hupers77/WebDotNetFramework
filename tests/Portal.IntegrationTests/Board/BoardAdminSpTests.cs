using Portal.Data;
using Portal.IntegrationTests.Infrastructure;
using Portal.Web.Areas.Board.Models;
using Portal.Web.Areas.Board.Services;

namespace Portal.IntegrationTests.Board;

/// <summary>게시판 관리 SP: 설정·이름 저장, 동시성 충돌, 없는 게시판.</summary>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class BoardAdminSpTests(DbFixture fixture)
{
    private static BoardAdminSaveRow ToRow(BoardAdminDto b, Action<BoardAdminSaveRow> change)
    {
        var row = new BoardAdminSaveRow
        {
            RowStat = "U", BoardCd = b.BoardCd, NameKo = b.NameKo!, NameEn = b.NameEn!, NameZh = b.NameZh!, ReplyYn = b.ReplyYn, CommentYn = b.CommentYn,
            AttachYn = b.AttachYn, AnonYn = b.AnonYn, SecretYn = b.SecretYn, PeriodYn = b.PeriodYn, PinYn = b.PinYn, ReaderYn = b.ReaderYn,
            SortNo = b.SortNo, UseYn = b.UseYn, RowVer = b.RowVer,
        };
        change(row);
        return row;
    }

    [Fact]
    public async Task 저장하면_설정과_이름이_바뀌고_이전_RowVer로_다시_저장하면_충돌()
    {
        await using var db = await fixture.BeginAsync();
        var service = new BoardAdminService(db.Sp);
        var notice = (await service.GetListAsync(TestContext.Current.CancellationToken)).Items.Single(b => b.BoardCd == "Notice");

        await service.SaveAsync(new BoardAdminSaveRequest { Rows = [ToRow(notice, r => { r.CommentYn = "Y"; r.NameEn = "News"; })] }, TestContext.Current.CancellationToken);
        var after = (await service.GetListAsync(TestContext.Current.CancellationToken)).Items.Single(b => b.BoardCd == "Notice");

        Assert.Equal(("Y", "News"), (after.CommentYn, after.NameEn));
        await Assert.ThrowsAsync<Portal.Core.Errors.BizException>(() =>
            service.SaveAsync(new BoardAdminSaveRequest { Rows = [ToRow(notice, r => r.PinYn = "N")] }, TestContext.Current.CancellationToken));
    }
}
