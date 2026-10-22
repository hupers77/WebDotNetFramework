using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Portal.Core.Errors;
using Portal.Infrastructure.Excel;

namespace Portal.UnitTests.Infrastructure;

public sealed class ExcelReaderTests
{
    private sealed record Item(string Code, string? Name, int Sort, DateOnly Day);

    private static Stream Written(params Item[] rows) => ExcelWriter.Write(new ExcelSheet<Item>("목록",
    [
        new("코드", i => i.Code),
        new("명칭", i => i.Name),
        new("정렬", i => i.Sort),
        new("일자", i => i.Day),
    ], rows));

    [Fact]
    public void ExcelWriter로쓴파일_머리글과값을그대로읽는다()
    {
        using var stream = Written(new Item("A", "가", 10, new DateOnly(2026, 10, 22)), new Item("B", null, 20, new DateOnly(2026, 1, 1)));

        var table = ExcelReader.Read(stream, 10);

        Assert.Equal(["코드", "명칭", "정렬", "일자"], table.Headers);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(2, table.Rows[0].RowNo);
        Assert.Equal(["A", "가", "10", "2026-10-22"], table.Rows[0].Values);
        Assert.Equal(["B", null, "20", "2026-01-01"], table.Rows[1].Values);
    }

    [Fact]
    public void 엑셀이저장한형식_공유문자열_날짜서식_논리값_빈칸건너뜀()
    {
        var day = new DateTime(2026, 3, 5).ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var stream = Build(
            new Row(Shared("A1", 0), Shared("B1", 1), Shared("D1", 2)) { RowIndex = 1 },
            new Row(Shared("A2", 3), new Cell { CellReference = "D2", CellValue = new CellValue(day), StyleIndex = 1 }) { RowIndex = 2 },
            new Row(new Cell { CellReference = "A3", DataType = CellValues.String, CellValue = new CellValue("  ") }) { RowIndex = 3 },
            new Row(Shared("A5", 3), new Cell { CellReference = "B5", DataType = CellValues.Boolean, CellValue = new CellValue("1") }) { RowIndex = 5 });

        var table = ExcelReader.Read(stream, 10);

        Assert.Equal(["코드", "여부", "", "일자"], table.Headers);
        Assert.Equal([2, 5], table.Rows.Select(r => r.RowNo));
        Assert.Equal(["X", null, null, "2026-03-05"], table.Rows[0].Values);
        Assert.Equal("Y", table.Rows[1][1]);
        Assert.Null(table.Rows[1][9]);
    }

    [Fact]
    public void 행수한도초과_업무오류()
    {
        using var stream = Written(new Item("A", "가", 1, default), new Item("B", "나", 2, default));

        var ex = Assert.Throws<BizException>(() => ExcelReader.Read(stream, 1));

        Assert.Equal(ExcelMessageKeys.TooManyRows, ex.MessageKey);
    }

    [Theory]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D })]
    [InlineData(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00 })]
    [InlineData(new byte[0])]
    public void 엑셀이아닌파일_업무오류(byte[] content)
    {
        var ex = Assert.Throws<BizException>(() => ExcelReader.Read(new MemoryStream(content), 10));

        Assert.Equal(ExcelMessageKeys.Invalid, ex.MessageKey);
    }

    [Theory]
    [InlineData("A1", 0)]
    [InlineData("Z9", 25)]
    [InlineData("AB12", 27)]
    public void 칸주소_열번호(string reference, int index) => Assert.Equal(index, ExcelReader.ColumnIndex(reference));

    private static Cell Shared(string reference, int index) =>
        new() { CellReference = reference, DataType = CellValues.SharedString, CellValue = new CellValue(index) };

    /// <summary>엑셀 프로그램처럼 공유 문자열과 기본 날짜 서식(14)을 쓰는 파일.</summary>
    private static MemoryStream Build(params Row[] rows)
    {
        var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var book = doc.AddWorkbookPart();
            book.Workbook = new Workbook();
            var strings = book.AddNewPart<SharedStringTablePart>();
            strings.SharedStringTable = new SharedStringTable(
                new SharedStringItem(new Text("코드")), new SharedStringItem(new Text("여부")),
                new SharedStringItem(new Text(" 일자 ")), new SharedStringItem(new Text("X")));
            var styles = book.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = new Stylesheet(
                new Fonts(new Font()), new Fills(new Fill()), new Borders(new Border()),
                new CellFormats(new CellFormat(), new CellFormat { NumberFormatId = 14, ApplyNumberFormat = true }));
            var sheet = book.AddNewPart<WorksheetPart>();
            sheet.Worksheet = new Worksheet(new SheetData(rows.Cast<OpenXmlElement>()));
            book.Workbook.AppendChild(new Sheets(new Sheet { Id = book.GetIdOfPart(sheet), SheetId = 1, Name = "S" }));
        }

        stream.Position = 0;
        return stream;
    }
}
