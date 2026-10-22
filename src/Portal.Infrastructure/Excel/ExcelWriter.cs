using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Portal.Infrastructure.Excel;

/// <summary>
/// 엑셀(xlsx) 쓰기 (설계서 2장 Open XML SDK, 12장 엑셀 출력). 행을 차례로 흘려 쓰므로(SAX) 행이 많아도 메모리를 적게 쓴다.
/// 문자열은 항상 글자 칸으로 써서 <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>로 시작해도 수식으로 실행되지 않는다(수식 주입 방지).
/// </summary>
public static class ExcelWriter
{
    /// <summary>xlsx 콘텐츠 형식.</summary>
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const uint HeaderStyle = 1;
    private const uint DateStyle = 2;
    private const uint DateTimeStyle = 3;
    private static readonly DateTime ExcelEpochSafe = new(1900, 3, 1);

    /// <summary>
    /// 시트를 임시 파일에 써서 읽기 스트림으로 돌려준다(처음 위치). 스트림을 닫으면 임시 파일이 지워진다.
    /// </summary>
    public static Stream Write<T>(ExcelSheet<T> sheet)
    {
        var path = Path.Combine(Path.GetTempPath(), "portal-xlsx-" + Guid.NewGuid().ToString("N"));
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
        try
        {
            using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
            {
                var workbook = document.AddWorkbookPart();
                workbook.Workbook = new Workbook();
                workbook.AddNewPart<WorkbookStylesPart>().Stylesheet = Styles();
                var sheetPart = workbook.AddNewPart<WorksheetPart>();
                WriteSheet(sheetPart, sheet);
                workbook.Workbook.AppendChild(new Sheets(new Sheet
                {
                    Id = workbook.GetIdOfPart(sheetPart),
                    SheetId = 1,
                    Name = SheetName(sheet.Name),
                }));
                workbook.Workbook.Save();
            }

            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static void WriteSheet<T>(WorksheetPart part, ExcelSheet<T> sheet)
    {
        using var writer = OpenXmlWriter.Create(part);
        writer.WriteStartElement(new Worksheet());

        // 머리글 행 고정
        writer.WriteStartElement(new SheetViews());
        writer.WriteStartElement(new SheetView { WorkbookViewId = 0 });
        writer.WriteElement(new Pane { VerticalSplit = 1, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen });
        writer.WriteEndElement();
        writer.WriteEndElement();

        writer.WriteStartElement(new Columns());
        for (var i = 0; i < sheet.Columns.Count; i++)
        {
            var column = sheet.Columns[i];
            var width = column.Width ?? Math.Clamp(DisplayLength(column.Header) + 4, 10, 60);
            writer.WriteElement(new Column { Min = (uint)(i + 1), Max = (uint)(i + 1), Width = width, CustomWidth = true });
        }

        writer.WriteEndElement();

        writer.WriteStartElement(new SheetData());
        writer.WriteStartElement(new Row());
        foreach (var column in sheet.Columns)
        {
            writer.WriteElement(TextCell(column.Header, HeaderStyle));
        }

        writer.WriteEndElement();

        foreach (var row in sheet.Rows)
        {
            writer.WriteStartElement(new Row());
            foreach (var column in sheet.Columns)
            {
                writer.WriteElement(ValueCell(column.Value(row)));
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    /// <summary>값 형식에 맞는 칸. 날짜는 엑셀 날짜(일련번호 + 서식)로 써서 정렬·필터가 된다.</summary>
    internal static Cell ValueCell(object? value) => value switch
    {
        null => new Cell(),
        string s => TextCell(s, 0),
        bool b => TextCell(b ? "Y" : "N", 0),
        DateOnly d => DateCell(d.ToDateTime(TimeOnly.MinValue)),
        DateTime dt => DateCell(dt),
        byte or short or int or long or decimal or double or float => NumberCell(Convert.ToDouble(value, CultureInfo.InvariantCulture), 0),
        _ => TextCell(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "", 0),
    };

    /// <summary>
    /// 엑셀은 1900-02-29가 있는 것으로 세므로(1900년 윤년 오류) 1900-03-01 이전 날짜는 일련번호가 하루 어긋난다.
    /// 그런 날짜(시작일 기본값 1900-01-01 등)는 글자 칸으로 쓴다.
    /// </summary>
    private static Cell DateCell(DateTime value) =>
        value < ExcelEpochSafe
            ? TextCell(value.ToString(value.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), 0)
            : NumberCell(value.ToOADate(), value.TimeOfDay == TimeSpan.Zero ? DateStyle : DateTimeStyle);

    private static Cell TextCell(string text, uint style) => new(new InlineString(new Text(Clean(text)) { Space = SpaceProcessingModeValues.Preserve }))
    {
        DataType = CellValues.InlineString,
        StyleIndex = style == 0 ? null : style,
    };

    private static Cell NumberCell(double number, uint style) => new(new CellValue(number))
    {
        DataType = CellValues.Number,
        StyleIndex = style == 0 ? null : style,
    };

    /// <summary>XML에 쓸 수 없는 제어 문자를 뺀다 (탭·줄바꿈은 둔다). 엑셀 칸 한도(32,767자)로 자른다.</summary>
    internal static string Clean(string text)
    {
        var clean = string.Create(text.Length, text, (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                span[i] = char.IsControl(c) && c is not '\t' and not '\n' and not '\r' ? ' ' : c;
            }
        });
        return clean.Length > 32767 ? clean[..32767] : clean;
    }

    /// <summary>시트 이름 규칙: 31자까지, <c>\ / ? * [ ] :</c> 금지, 비면 Sheet1.</summary>
    internal static string SheetName(string name)
    {
        var clean = new string([.. name.Select(c => "\\/?*[]:".Contains(c, StringComparison.Ordinal) ? '_' : c)]).Trim('\'', ' ');
        return clean.Length == 0 ? "Sheet1" : clean.Length > 31 ? clean[..31] : clean;
    }

    /// <summary>한글·한자는 두 칸으로 센다 (열 너비 계산용).</summary>
    private static double DisplayLength(string text) => text.Sum(c => c > 0x2E80 ? 2 : 1);

    /// <summary>서식: 0 기본, 1 머리글(굵게), 2 날짜, 3 일시. 요소 순서는 스키마 순서(numFmts → fonts → fills → borders → cellXfs)여야 한다.</summary>
    private static Stylesheet Styles() => new(
        new NumberingFormats(
            new NumberingFormat { NumberFormatId = 164, FormatCode = "yyyy-mm-dd" },
            new NumberingFormat { NumberFormatId = 165, FormatCode = "yyyy-mm-dd hh:mm" }) { Count = 2 },
        new Fonts(new Font(), new Font(new Bold())) { Count = 2 },
        new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2 },
        new Borders(new Border()) { Count = 1 },
        new CellFormats(
            new CellFormat(),
            new CellFormat { FontId = 1, ApplyFont = true },
            new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true },
            new CellFormat { NumberFormatId = 165, ApplyNumberFormat = true }) { Count = 4 });
}
