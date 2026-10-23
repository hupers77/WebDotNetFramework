using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Portal.Core.Errors;
using Portal.Web.Areas.Rpt.Models;

namespace Portal.Web.Areas.Rpt.Services;

/// <summary>
/// 레포트 조건 (<c>TB_REPORT.PARAMS_JSON</c>) 읽기·기본값·검사. 화면 입력은 문자열로 받아 형식을 확인한 뒤 SP 파라미터로 바꾼다.
/// 정의에 없는 값은 버리므로 화면이 공통 파라미터(<c>P_USER_ID</c> 등)나 다른 이름을 보내도 SP에 닿지 않는다.
/// </summary>
internal static partial class ReportParams
{
    /// <summary>날짜 (yyyy-MM-dd) → SP에 <see cref="DateOnly"/>.</summary>
    public const string Date = "DATE";

    /// <summary>월 (yyyy-MM) → SP에 문자열.</summary>
    public const string Month = "MONTH";

    /// <summary>글자 → SP에 문자열 (앞뒤 공백 제거, 최대 길이).</summary>
    public const string Text = "TEXT";

    /// <summary>프레임워크가 채우는 데이터 SP 파라미터 (조건 이름으로 쓸 수 없다).</summary>
    public const string MaxRowsParam = "P_MAX_ROWS";

    private const int DefaultTextLength = 100;

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal) { "P_USER_ID", "P_LANG_CD", "P_CLIENT_IP", MaxRowsParam };

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [GeneratedRegex("^P_[A-Z0-9_]{1,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])$", RegexOptions.CultureInvariant)]
    private static partial Regex MonthPattern();

    /// <summary>조건 정의를 읽는다. 잘못된 정의는 등록 오류이므로 <see cref="InvalidOperationException"/>.</summary>
    public static IReadOnlyList<ReportParam> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        List<Item>? items;
        try
        {
            items = JsonSerializer.Deserialize<List<Item>>(json, Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("TB_REPORT.PARAMS_JSON이 올바른 JSON 배열이 아닙니다.", ex);
        }

        var result = new List<ReportParam>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items ?? [])
        {
            var name = item.Name ?? "";
            var type = (item.Type ?? "").ToUpperInvariant();
            if (!NamePattern().IsMatch(name) || Reserved.Contains(name) || !names.Add(name))
            {
                throw new InvalidOperationException($"TB_REPORT.PARAMS_JSON: 조건 이름 '{name}'을 쓸 수 없습니다(P_대문자, 중복·공통 파라미터 불가).");
            }

            if (type is not (Date or Month or Text))
            {
                throw new InvalidOperationException($"TB_REPORT.PARAMS_JSON: '{name}'의 형식 '{item.Type}'은 지원하지 않습니다(DATE/MONTH/TEXT).");
            }

            var dflt = item.Default?.ToUpperInvariant();
            if (dflt is not (null or "" or "TODAY" or "THIS_MONTH" or "PREV_MONTH"))
            {
                throw new InvalidOperationException($"TB_REPORT.PARAMS_JSON: '{name}'의 기본값 '{item.Default}'은 지원하지 않습니다.");
            }

            result.Add(new ReportParam(name, type, item.Required, item.LabelKey ?? name, dflt is "" ? null : dflt, item.MaxLength is > 0 and <= 4000 ? item.MaxLength.Value : DefaultTextLength));
        }

        return result;
    }

    /// <summary>조건 입력란의 기본값 (사용자 오늘 날짜 기준, 입력란 값 형식).</summary>
    public static Dictionary<string, string?> Defaults(IReadOnlyList<ReportParam> parameters, DateOnly today)
    {
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        return parameters.ToDictionary(
            p => p.Name,
            p => (p.Type, p.Default) switch
            {
                (Date, "TODAY") => today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                (Date, "THIS_MONTH") => thisMonth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                (Date, "PREV_MONTH") => thisMonth.AddMonths(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                (Month, "TODAY" or "THIS_MONTH") => thisMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                (Month, "PREV_MONTH") => thisMonth.AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture),
                _ => null,
            },
            StringComparer.Ordinal);
    }

    /// <summary>
    /// 화면 입력을 검사해 SP 파라미터와 머리글에 찍을 조건으로 바꾼다. 필수 값이 없으면 <c>VAL.REQUIRED</c>,
    /// 형식이 틀리면 <c>RPT.PARAM_INVALID</c> 업무 오류(인자: 조건 이름 문구).
    /// </summary>
    public static (List<KeyValuePair<string, object?>> SpParameters, List<(string Label, string Value)> Conditions) Bind(
        IReadOnlyList<ReportParam> parameters, IReadOnlyDictionary<string, string?> input, Func<string, string> label)
    {
        var sp = new List<KeyValuePair<string, object?>>();
        var conditions = new List<(string, string)>();
        foreach (var p in parameters)
        {
            var raw = input.TryGetValue(p.Name, out var v) ? v?.Trim() : null;
            if (string.IsNullOrEmpty(raw))
            {
                if (p.Required)
                {
                    throw new BizException(ReportMessageKeys.ParamRequired, label(p.LabelKey));
                }

                sp.Add(new(p.Name, null));
                continue;
            }

            object value = p.Type switch
            {
                Date => DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    ? d
                    : throw new BizException(ReportMessageKeys.ParamInvalid, label(p.LabelKey)),
                Month => MonthPattern().IsMatch(raw) ? raw : throw new BizException(ReportMessageKeys.ParamInvalid, label(p.LabelKey)),
                _ => raw.Length <= p.MaxLength ? raw : throw new BizException(ReportMessageKeys.ParamInvalid, label(p.LabelKey)),
            };
            sp.Add(new(p.Name, value));
            conditions.Add((label(p.LabelKey), raw));
        }

        return (sp, conditions);
    }

    private sealed class Item
    {
        public string? Name { get; set; }

        public string? Type { get; set; }

        public bool Required { get; set; }

        public string? LabelKey { get; set; }

        public string? Default { get; set; }

        public int? MaxLength { get; set; }
    }
}
