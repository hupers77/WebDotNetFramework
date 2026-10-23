using Portal.Data;
using Portal.Web.Areas.Rpt.Models;
using Portal.Web.Areas.Rpt.Services;

namespace Portal.UnitTests.Rpt;

public sealed class ReportFormatTests
{
    private static readonly string[] Names = ["LOG_DATE", "OK_CNT", "AMT", "NOTE"];

    private static SpRow Row(int day, int? cnt, decimal amt, string? note) => new(Names, [new DateOnly(2026, 9, day), cnt, amt, note]);

    [Fact]
    public void Text_날짜_일시는현지시간_정수쉼표_소수둘째자리_null은빈칸()
    {
        var utc = new DateTime(2026, 9, 1, 3, 4, 0, DateTimeKind.Unspecified);

        Assert.Equal("2026-09-01", ReportFormat.Text(new DateOnly(2026, 9, 1)));
        Assert.Equal(DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture), ReportFormat.Text(utc));
        Assert.Equal("1,234,567", ReportFormat.Text(1234567L));
        Assert.Equal("1,234.5", ReportFormat.Text(1234.5m));
        Assert.Equal("0.33", ReportFormat.Text(1d / 3));
        Assert.Equal("", ReportFormat.Text(null));
        Assert.Equal("Y", ReportFormat.Text(true));
    }

    [Fact]
    public void Columns_숫자컬럼은값이모두숫자일때_첫컬럼제외_라벨함수()
    {
        var rows = new[] { Row(1, 2, 1.5m, "a"), Row(2, null, 2m, null) };

        var columns = ReportFormat.Columns(Names, rows, n => n.ToLowerInvariant());

        Assert.Equal(
            [new ReportColumn("LOG_DATE", "log_date", false), new("OK_CNT", "ok_cnt", true), new("AMT", "amt", true), new("NOTE", "note", false)],
            columns);
    }

    [Fact]
    public void Columns_값이모두null이면_숫자컬럼아님()
    {
        var columns = ReportFormat.Columns(Names, [Row(1, null, 1m, null)], n => n);

        Assert.False(columns[1].Numeric);
    }

    [Fact]
    public void Totals_첫컬럼은합계문구_숫자는합_나머지는null()
    {
        var rows = new[] { Row(1, 2, 1.5m, "a"), Row(2, null, 2m, null), Row(3, 5, 0.25m, "b") };
        var columns = ReportFormat.Columns(Names, rows, n => n);

        var totals = ReportFormat.Totals(columns, rows, "합계");

        Assert.Equal(["합계", 7m, 3.75m, null], totals.Values);
        Assert.Equal(Names, totals.Columns);
    }

    [Fact]
    public void Cell_일시만현지시간으로_나머지는그대로()
    {
        var utc = new DateTime(2026, 9, 1, 3, 0, 0);

        Assert.Equal(DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime(), ReportFormat.Cell(utc));
        Assert.Equal(new DateOnly(2026, 9, 1), ReportFormat.Cell(new DateOnly(2026, 9, 1)));
        Assert.Equal(3, ReportFormat.Cell(3));
    }
}
