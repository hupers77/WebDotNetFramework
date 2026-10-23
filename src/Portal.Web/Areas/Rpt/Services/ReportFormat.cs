using System.Globalization;
using Portal.Data;
using Portal.Web.Areas.Rpt.Models;

namespace Portal.Web.Areas.Rpt.Services;

/// <summary>레포트 표의 값 표시와 합계 (HTML·엑셀 공통, 단위 테스트 대상).</summary>
internal static class ReportFormat
{
    /// <summary>숫자 형식인지 (오른쪽 정렬, 합계 대상).</summary>
    public static bool IsNumber(object? value) =>
        value is byte or short or int or long or decimal or double or float;

    /// <summary>화면 출력의 칸 글자가 숫자인지 (<c>1,234</c>, <c>-5.5</c>, <c>12%</c>). 오른쪽 정렬에만 쓴다.</summary>
    public static bool LooksNumeric(string? text) =>
        text is { Length: > 0 and <= 30 } t && decimal.TryParse(t.Trim().TrimEnd('%'), NumberStyles.Number, CultureInfo.InvariantCulture, out _);

    /// <summary>
    /// 표에 찍을 글자. 날짜(<see cref="DateOnly"/>)는 yyyy-MM-dd, 일시(<see cref="DateTime"/>, UTC)는 서버 현지 시간 yyyy-MM-dd HH:mm,
    /// 정수는 천 단위 쉼표, 소수는 둘째 자리까지.
    /// </summary>
    public static string Text(object? value) => value switch
    {
        null => "",
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime dt => DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        byte or short or int or long => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString("#,0", CultureInfo.InvariantCulture),
        decimal or double or float => Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString("#,0.##", CultureInfo.InvariantCulture),
        bool b => b ? "Y" : "N",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    /// <summary>엑셀 칸 값. 일시는 서버 현지 시간으로 바꾸고, 나머지는 그대로 둔다(숫자·날짜 형식 유지).</summary>
    public static object? Cell(object? value) =>
        value is DateTime dt ? DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime() : value;

    /// <summary>컬럼 구성. 숫자 컬럼은 값이 하나라도 있고 모두 숫자인 컬럼이다(첫 컬럼은 항목 이름이므로 제외).</summary>
    public static List<ReportColumn> Columns(IReadOnlyList<string> names, IReadOnlyList<SpRow> rows, Func<string, string> label) =>
        [.. names.Select((name, i) => new ReportColumn(
            name,
            label(name),
            i > 0 && rows.Any(r => r[i] is not null) && rows.All(r => r[i] is null || IsNumber(r[i]))))];

    /// <summary>합계 행: 첫 컬럼은 <paramref name="totalLabel"/>, 숫자 컬럼은 합(decimal), 나머지는 null.</summary>
    public static SpRow Totals(IReadOnlyList<ReportColumn> columns, IReadOnlyList<SpRow> rows, string totalLabel)
    {
        var values = new object?[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            values[i] = i == 0 ? totalLabel
                : columns[i].Numeric ? rows.Sum(r => r[i] is null ? 0m : Convert.ToDecimal(r[i], CultureInfo.InvariantCulture))
                : null;
        }

        return new SpRow([.. columns.Select(c => c.Name)], values);
    }
}
