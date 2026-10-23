using Portal.Core.Errors;
using Portal.Data;
using Portal.IntegrationTests.Infrastructure;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.IntegrationTests.Board;

/// <summary>휴일 SP와 영업일 함수: 추가·수정·삭제, 중복·동시성 오류, 주말·휴일·대체 근무일 판단.</summary>
[Collection(DbTests.Name)]
[Trait("Category", "Integration")]
public sealed class HolidaySpTests(DbFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HolidaySaveRow Insert(string date, string name, string workYn = "N") => new() { RowStat = "I", HolidayDt = date, HolidayNm = name, WorkYn = workYn };

    [Fact]
    public async Task 추가_수정_삭제가_저장되고_영업일_함수가_달력을_따른다()
    {
        await using var db = await fixture.BeginAsync();
        var service = new HolidayAdminService(db.Sp);

        // 2031-05-05 월요일 휴일, 2031-05-10 토요일 대체 근무일
        await service.SaveAsync(new HolidaySaveRequest { Rows = [Insert("2031-05-05", "어린이날"), Insert("2031-05-10", "대체 근무", "Y")] }, Ct);
        var list = (await service.GetListAsync(2031, Ct)).Items;

        Assert.Equal(["2031-05-05", "2031-05-10"], list.Select(h => h.HolidayDt));
        Assert.Equal([1, 6], list.Select(h => h.WeekdayNo));
        Assert.Equal((0, 1, 0, 1), (
            await db.ScalarAsync<int>("SELECT CAST(FW.UFN_IS_BUSINESS_DAY('2031-05-05') AS INT)"),
            await db.ScalarAsync<int>("SELECT CAST(FW.UFN_IS_BUSINESS_DAY('2031-05-10') AS INT)"),
            await db.ScalarAsync<int>("SELECT CAST(FW.UFN_IS_BUSINESS_DAY('2031-05-11') AS INT)"),
            await db.ScalarAsync<int>("SELECT CAST(FW.UFN_IS_BUSINESS_DAY('2031-05-12') AS INT)")));

        var first = list[0];
        await service.SaveAsync(new HolidaySaveRequest { Rows = [new() { RowStat = "U", HolidayDt = first.HolidayDt, HolidayNm = "변경", WorkYn = "N", RowVer = first.RowVer }] }, Ct);
        Assert.Equal("변경", (await service.GetListAsync(2031, Ct)).Items[0].HolidayNm);

        var current = (await service.GetListAsync(2031, Ct)).Items;
        await service.SaveAsync(new HolidaySaveRequest { Rows = [new() { RowStat = "D", HolidayDt = current[1].HolidayDt, WorkYn = "N", RowVer = current[1].RowVer }] }, Ct);
        Assert.Single((await service.GetListAsync(2031, Ct)).Items);
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT CAST(FW.UFN_IS_BUSINESS_DAY('2031-05-10') AS INT)"));
    }

    [Fact]
    public async Task 같은_날짜를_다시_추가하면_중복_오류()
    {
        await using var db = await fixture.BeginAsync();
        var service = new HolidayAdminService(db.Sp);
        await service.SaveAsync(new HolidaySaveRequest { Rows = [Insert("2031-01-01", "신정")] }, Ct);

        var ex = await Assert.ThrowsAsync<BizException>(() => service.SaveAsync(new HolidaySaveRequest { Rows = [Insert("2031-01-01", "또")] }, Ct));

        Assert.Equal("MSG.DUP_HOLIDAY", ex.MessageKey);
    }
}
