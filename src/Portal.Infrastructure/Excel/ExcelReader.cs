using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Portal.Core.Errors;
using Portal.Infrastructure.Files;

namespace Portal.Infrastructure.Excel;

/// <summary>엑셀 첫 시트의 머리글과 행. 값은 모두 글자이고, 빈 칸은 null이다.</summary>
/// <param name="Headers">첫 행 (앞뒤 공백 제거).</param>
/// <param name="Rows">둘째 행부터 값이 하나라도 있는 행.</param>
public sealed record ExcelTable(IReadOnlyList<string> Headers, IReadOnlyList<ExcelRow> Rows);

/// <summary>엑셀 행 하나.</summary>
/// <param name="RowNo">엑셀 행 번호 (오류 안내용, 머리글이 1).</param>
/// <param name="Values">머리글 순서의 값.</param>
public sealed record ExcelRow(int RowNo, IReadOnlyList<string?> Values)
{
    /// <summary>머리글 위치의 값. 없으면 null.</summary>
    public string? this[int index] => index >= 0 && index < Values.Count ? Values[index] : null;
}

/// <summary>엑셀 오류 메시지 키.</summary>
public static class ExcelMessageKeys
{
    /// <summary>엑셀 파일(xlsx)을 읽을 수 없습니다.</summary>
    public const string Invalid = "MSG.EXCEL_INVALID";

    /// <summary>한 번에 올릴 수 있는 행은 {0}개까지입니다.</summary>
    public const string TooManyRows = "MSG.EXCEL_TOO_MANY_ROWS";

    /// <summary>양식의 머리글이 맞지 않습니다: {0}</summary>
    public const string HeaderMismatch = "MSG.EXCEL_HEADER_MISMATCH";
}

/// <summary>
/// 엑셀(xlsx) 읽기 (엑셀 업로드 표준 화면 8). 첫 시트만 흘려 읽고(SAX), 행 수·파트 크기에 한도를 둔다(압축 폭탄 방지).
/// 수식 칸은 수식이 아니라 저장된 결과 값만 읽는다. 날짜 서식 칸은 <c>yyyy-MM-dd</c>(시각이 있으면 <c>yyyy-MM-dd HH:mm</c>)로 바꾼다.
/// </summary>
public static class ExcelReader
{
    /// <summary>파트 하나의 최대 글자 수 (압축을 풀었을 때 크기 한도).</summary>
    private const long MaxCharactersInPart = 50_000_000;

    /// <summary>
    /// 첫 시트를 읽는다. 파일 형식이 아니면 <see cref="ExcelMessageKeys.Invalid"/>, 행이 <paramref name="maxRows"/>보다 많으면
    /// <see cref="ExcelMessageKeys.TooManyRows"/> 업무 오류. 스트림은 위치를 옮길 수 있어야 한다.
    /// </summary>
    public static ExcelTable Read(Stream content, int maxRows)
    {
        var head = new byte[4];
        if (content.Read(head) < 4 || !FileSignatures.Matches("xlsx", head))
        {
            throw new BizException(ExcelMessageKeys.Invalid);
        }

        content.Position = 0;
        try
        {
            using var document = SpreadsheetDocument.Open(content, false, new OpenSettings { MaxCharactersInPart = MaxCharactersInPart });
            var workbook = document.WorkbookPart ?? throw new BizException(ExcelMessageKeys.Invalid);
            var first = workbook.Workbook?.Sheets?.Elements<Sheet>().FirstOrDefault()?.Id?.Value ?? throw new BizException(ExcelMessageKeys.Invalid);
            var sheet = (WorksheetPart)workbook.GetPartById(first);
            var shared = workbook.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(i => i.InnerText).ToArray() ?? [];
            var dateStyles = DateStyles(workbook.WorkbookStylesPart?.Stylesheet);
            return ReadSheet(sheet, shared, dateStyles, maxRows);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or FormatException or System.Xml.XmlException or IOException or InvalidOperationException)
        {
            throw new BizException(ExcelMessageKeys.Invalid);
        }
    }

    private static ExcelTable ReadSheet(WorksheetPart sheet, string[] shared, HashSet<uint> dateStyles, int maxRows)
    {
        List<string>? headers = null;
        var rows = new List<ExcelRow>();
        using var reader = OpenXmlReader.Create(sheet);
        while (reader.Read())
        {
            if (reader.ElementType != typeof(Row) || !reader.IsStartElement)
            {
                continue;
            }

            var row = (Row)reader.LoadCurrentElement()!;
            var rowNo = (int)(row.RowIndex?.Value ?? (uint)(rows.Count + (headers is null ? 1 : 2)));
            var values = new SortedDictionary<int, string?>();
            var next = 0;
            foreach (var cell in row.Elements<Cell>())
            {
                var index = cell.CellReference?.Value is { } reference ? ColumnIndex(reference) : next;
                next = index + 1;
                values[index] = CellText(cell, shared, dateStyles);
            }

            if (headers is null)
            {
                var width = values.Count == 0 ? 0 : values.Keys.Max() + 1;
                headers = [.. Enumerable.Range(0, width).Select(i => values.GetValueOrDefault(i)?.Trim() ?? "")];
                continue;
            }

            if (values.Values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (rows.Count >= maxRows)
            {
                throw new BizException(ExcelMessageKeys.TooManyRows, maxRows);
            }

            rows.Add(new ExcelRow(rowNo, [.. Enumerable.Range(0, headers.Count).Select(i => values.GetValueOrDefault(i))]));
        }

        return new ExcelTable(headers ?? [], rows);
    }

    private static string? CellText(Cell cell, string[] shared, HashSet<uint> dateStyles)
    {
        var raw = cell.DataType?.Value == CellValues.InlineString ? cell.InlineString?.InnerText : cell.CellValue?.Text;
        if (raw is null)
        {
            return null;
        }

        string? text;
        if (cell.DataType?.Value == CellValues.SharedString)
        {
            text = int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i < shared.Length ? shared[i] : null;
        }
        else if (cell.DataType?.Value == CellValues.Boolean)
        {
            text = raw == "1" ? "Y" : "N";
        }
        else if ((cell.DataType is null || cell.DataType.Value == CellValues.Number)
                 && cell.StyleIndex?.Value is { } style && dateStyles.Contains(style)
                 && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
                 && serial is >= 61 and < 2958466)
        {
            var date = DateTime.FromOADate(serial);
            text = date.ToString(date.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }
        else
        {
            text = raw;
        }

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>"AB12" → 27 (0부터).</summary>
    internal static int ColumnIndex(string reference)
    {
        var index = 0;
        foreach (var c in reference)
        {
            if (c is < 'A' or > 'Z')
            {
                break;
            }

            index = (index * 26) + (c - 'A' + 1);
        }

        return index - 1;
    }

    /// <summary>날짜 서식 칸 스타일 번호. 기본 날짜 서식(14~22, 45~47)과 y·d가 들어간 사용자 서식.</summary>
    private static HashSet<uint> DateStyles(Stylesheet? styles)
    {
        var custom = styles?.NumberingFormats?.Elements<NumberingFormat>()
            .Where(f => f.FormatCode?.Value is { } code && IsDateFormat(code))
            .Select(f => f.NumberFormatId!.Value)
            .ToHashSet() ?? [];
        var result = new HashSet<uint>();
        var index = 0u;
        foreach (var format in styles?.CellFormats?.Elements<CellFormat>() ?? [])
        {
            var id = format.NumberFormatId?.Value ?? 0;
            if (id is >= 14 and <= 22 or >= 45 and <= 47 || custom.Contains(id))
            {
                result.Add(index);
            }

            index++;
        }

        return result;
    }

    private static bool IsDateFormat(string code)
    {
        // 따옴표 안 글자와 [색] 같은 꾸밈을 빼고 y·d가 남으면 날짜
        var plain = System.Text.RegularExpressions.Regex.Replace(code, "\"[^\"]*\"|\\[[^\\]]*\\]", "");
        return plain.Contains('y', StringComparison.OrdinalIgnoreCase) || plain.Contains('d', StringComparison.OrdinalIgnoreCase);
    }
}
