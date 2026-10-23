using Portal.Core.Errors;
using Portal.Web.Areas.Rpt.Models;
using Portal.Web.Areas.Rpt.Services;

namespace Portal.UnitTests.Rpt;

public sealed class ScreenPrintServiceTests
{
    private static ScreenPrintRequest Request(int tables = 1, int columns = 2, int rows = 1, int cellLength = 3) => new()
    {
        ProgramId = "X",
        Tables = [.. Enumerable.Range(0, tables).Select(_ => new ScreenTable
        {
            Columns = [.. Enumerable.Range(0, columns).Select(i => $"C{i}")],
            Rows = [.. Enumerable.Range(0, rows).Select(_ => Enumerable.Range(0, columns).Select(_ => (string?)new string('a', cellLength)).ToList())],
        })],
    };

    [Fact]
    public void Validate_한도안이면통과()
    {
        ScreenPrintService.Validate(Request(tables: 5, columns: 60, rows: 1000, cellLength: 2000));
    }

    [Theory]
    [InlineData(0, 2, 1, 3)]
    [InlineData(6, 2, 1, 3)]
    [InlineData(1, 0, 1, 3)]
    [InlineData(1, 61, 1, 3)]
    [InlineData(1, 2, 1, 2001)]
    public void Validate_모양이틀리면_입력오류(int tables, int columns, int rows, int cellLength)
    {
        var ex = Assert.Throws<BizException>(() => ScreenPrintService.Validate(Request(tables, columns, rows, cellLength)));

        Assert.Equal("MSG.INVALID_INPUT", ex.MessageKey);
    }

    [Fact]
    public void Validate_칸수가머리글과다르면_입력오류()
    {
        var request = Request();
        request.Tables[0].Rows.Add(["only one"]);

        Assert.Equal("MSG.INVALID_INPUT", Assert.Throws<BizException>(() => ScreenPrintService.Validate(request)).MessageKey);
    }

    [Fact]
    public void Validate_행합이5000을넘으면_조건을좁히라는오류()
    {
        var ex = Assert.Throws<BizException>(() => ScreenPrintService.Validate(Request(tables: 2, rows: 2501)));

        Assert.Equal((ReportMessageKeys.TooMany, (object?)ScreenPrintService.MaxRows), (ex.MessageKey, ex.Args[0]));
    }

    [Theory]
    [InlineData("1,234", true)]
    [InlineData("-5.5", true)]
    [InlineData("12%", true)]
    [InlineData("2026-10-01", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksNumeric_숫자글자만(string? text, bool expected)
    {
        Assert.Equal(expected, ReportFormat.LooksNumeric(text));
    }
}
