using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using Portal.Infrastructure.Excel;

namespace Portal.UnitTests.Infrastructure;

public sealed class ExcelWriterTests
{
    private sealed record Item(string? Name, int Count, DateOnly Day, DateTime At, bool Flag);

    private static readonly ExcelColumn<Item>[] Columns =
    [
        new("이름", i => i.Name),
        new("수량", i => i.Count),
        new("일자", i => i.Day),
        new("일시", i => i.At),
        new("여부", i => i.Flag),
    ];

    private static SpreadsheetDocument Write(params Item[] rows)
    {
        var stream = ExcelWriter.Write(new ExcelSheet<Item>("코드/목록", Columns, rows));
        return SpreadsheetDocument.Open(stream, false);
    }

    private static List<Row> Rows(SpreadsheetDocument doc) =>
        [.. doc.WorkbookPart!.WorksheetParts.Single().Worksheet!.Descendants<Row>()];

    private static string? Text(Cell cell) => cell.InlineString?.Text?.Text ?? cell.CellValue?.Text;

    [Fact]
    public void 머리글과값_형식대로쓴다_스키마검증통과()
    {
        using var doc = Write(new Item("견적", 12, new DateOnly(2026, 10, 22), new DateTime(2026, 10, 22, 9, 30, 0), true));

        Assert.Empty(new OpenXmlValidator().Validate(doc));
        var rows = Rows(doc);
        Assert.Equal(["이름", "수량", "일자", "일시", "여부"], rows[0].Elements<Cell>().Select(Text));
        Assert.All(rows[0].Elements<Cell>(), c => Assert.Equal(1u, c.StyleIndex!.Value));
        var cells = rows[1].Elements<Cell>().ToList();
        Assert.Equal("견적", Text(cells[0]));
        Assert.Equal(CellValues.Number, cells[1].DataType!.Value);
        Assert.Equal("12", Text(cells[1]));
        Assert.Equal(new DateOnly(2026, 10, 22).ToDateTime(TimeOnly.MinValue).ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture), Text(cells[2]));
        Assert.Equal(2u, cells[2].StyleIndex!.Value);
        Assert.Equal(3u, cells[3].StyleIndex!.Value);
        Assert.Equal("Y", Text(cells[4]));
        Assert.Equal("코드_목록", doc.WorkbookPart!.Workbook!.Descendants<Sheet>().Single().Name!.Value);
        Assert.NotNull(doc.WorkbookPart.WorksheetParts.Single().Worksheet!.Descendants<Pane>().Single(p => p.State! == PaneStateValues.Frozen));
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+cmd|' /C calc'!A0")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1)")]
    public void 수식처럼보이는문자열도_글자칸으로써서실행되지않는다(string value)
    {
        using var doc = Write(new Item(value, 0, default, default, false));

        var cell = Rows(doc)[1].Elements<Cell>().First();
        Assert.Equal(CellValues.InlineString, cell.DataType!.Value);
        Assert.Null(cell.CellFormula);
        Assert.Equal(value, Text(cell));
    }

    [Fact]
    public void 빈값은빈칸_제어문자는공백()
    {
        using var doc = Write(new Item(null, 0, default, default, false), new Item("a\u0001b\tc", 0, default, default, false));

        var rows = Rows(doc);
        Assert.Null(Text(rows[1].Elements<Cell>().First()));
        Assert.Equal("a b\tc", Text(rows[2].Elements<Cell>().First()));
    }

    [Theory]
    [InlineData("", "Sheet1")]
    [InlineData("a[b]:c*?", "a_b__c__")]
    [InlineData("1234567890123456789012345678901234", "1234567890123456789012345678901")]
    public void 시트이름규칙(string name, string expected) =>
        Assert.Equal(expected, ExcelWriter.SheetName(name));

    [Fact]
    public void 엑셀이틀리게세는1900년3월전날짜는_글자로쓴다()
    {
        using var doc = Write(new Item("a", 0, new DateOnly(1900, 1, 1), new DateTime(9999, 12, 31), false));

        var cells = Rows(doc)[1].Elements<Cell>().ToList();
        Assert.Equal((CellValues.InlineString, "1900-01-01"), (cells[2].DataType!.Value, Text(cells[2])));
        Assert.Equal(CellValues.Number, cells[3].DataType!.Value);
    }
}
