namespace Portal.Infrastructure.Scheduling;

/// <summary>
/// 배치 작업 주기(<c>TB_JOB.CRON_EXPR</c>). 표준 5필드 Cron: <c>분 시 일 월 요일</c>.
/// 필드마다 <c>*</c>, 값(<c>5</c>), 범위(<c>1-5</c>), 목록(<c>0,30</c>), 간격(<c>*/10</c>, <c>8-18/2</c>)을 쓸 수 있다. 요일은 0~7(0과 7이 일요일).
/// 일과 요일을 둘 다 지정하면 둘 중 하나만 맞아도 실행한다(표준 Cron 규칙).
/// </summary>
/// <example><c>* * * * *</c> 1분마다, <c>*/10 * * * *</c> 10분마다, <c>0 5 * * *</c> 매일 05:00, <c>0 5 1 * *</c> 매월 1일 05:00, <c>30 8 * * 1-5</c> 평일 08:30.</example>
public sealed class CronSchedule
{
    // 2월 29일만 지정한 주기도 찾을 수 있게 4년 넘게 본다
    private const int MaxSearchDays = 366 * 5;

    private readonly bool[] _minutes;
    private readonly bool[] _hours;
    private readonly bool[] _days;
    private readonly bool[] _months;
    private readonly bool[] _weekdays;
    private readonly bool _anyDay;
    private readonly bool _anyWeekday;

    private CronSchedule(string expression, bool[] minutes, bool[] hours, bool[] days, bool[] months, bool[] weekdays, bool anyDay, bool anyWeekday)
    {
        Expression = expression;
        _minutes = minutes;
        _hours = hours;
        _days = days;
        _months = months;
        _weekdays = weekdays;
        _anyDay = anyDay;
        _anyWeekday = anyWeekday;
    }

    /// <summary>원래 식.</summary>
    public string Expression { get; }

    /// <summary>식을 읽는다. 형식이 틀리면 <see cref="FormatException"/>.</summary>
    public static CronSchedule Parse(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var fields = expression.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
        {
            throw new FormatException($"Cron 식은 '분 시 일 월 요일' 5개 필드여야 합니다: '{expression}'");
        }

        var weekdays = ParseField(fields[4], 0, 7, expression);
        weekdays[0] |= weekdays[7];
        return new CronSchedule(
            expression.Trim(),
            ParseField(fields[0], 0, 59, expression),
            ParseField(fields[1], 0, 23, expression),
            ParseField(fields[2], 1, 31, expression),
            ParseField(fields[3], 1, 12, expression),
            weekdays[..7],
            fields[2] == "*",
            fields[4] == "*");
    }

    /// <summary>식을 읽는다. 비었거나 형식이 틀리면 false.</summary>
    public static bool TryParse(string? expression, out CronSchedule? schedule)
    {
        schedule = null;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        try
        {
            schedule = Parse(expression);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// <paramref name="afterUtc"/>보다 뒤인 첫 실행 시각(UTC, 분 단위). 시각은 <paramref name="timeZone"/>의 현지 시간으로 맞춘다.
    /// 일광 절약 시간으로 없는 현지 시각은 건너뛴다. 맞는 날이 없으면(예: 2월 30일) null.
    /// </summary>
    public DateTime? NextUtc(DateTime afterUtc, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc), timeZone);
        var start = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Unspecified).AddMinutes(1);

        for (var day = start.Date; (day - start.Date).TotalDays <= MaxSearchDays; day = day.AddDays(1))
        {
            if (!_months[day.Month] || !DayMatches(day))
            {
                continue;
            }

            var firstDay = day == start.Date;
            for (var hour = firstDay ? start.Hour : 0; hour < 24; hour++)
            {
                if (!_hours[hour])
                {
                    continue;
                }

                for (var minute = firstDay && hour == start.Hour ? start.Minute : 0; minute < 60; minute++)
                {
                    if (!_minutes[minute])
                    {
                        continue;
                    }

                    var candidate = day.AddHours(hour).AddMinutes(minute);
                    if (!timeZone.IsInvalidTime(candidate))
                    {
                        return TimeZoneInfo.ConvertTimeToUtc(candidate, timeZone);
                    }
                }
            }
        }

        return null;
    }

    private bool DayMatches(DateTime day)
    {
        var dayOk = _days[day.Day];
        var weekdayOk = _weekdays[(int)day.DayOfWeek];
        if (_anyDay || _anyWeekday)
        {
            return dayOk && weekdayOk;
        }

        return dayOk || weekdayOk;
    }

    private static bool[] ParseField(string field, int min, int max, string expression)
    {
        var allowed = new bool[max + 1];
        foreach (var part in field.Split(','))
        {
            var range = part;
            var step = 1;
            var slash = part.IndexOf('/', StringComparison.Ordinal);
            if (slash >= 0)
            {
                step = ParseNumber(part[(slash + 1)..], 1, max, expression);
                range = part[..slash];
            }

            int from;
            int to;
            if (range == "*")
            {
                (from, to) = (min, max);
            }
            else if (range.IndexOf('-', StringComparison.Ordinal) is var dash and > 0)
            {
                from = ParseNumber(range[..dash], min, max, expression);
                to = ParseNumber(range[(dash + 1)..], min, max, expression);
                if (from > to)
                {
                    throw new FormatException($"Cron 범위의 시작이 끝보다 큽니다: '{part}' ('{expression}')");
                }
            }
            else
            {
                from = ParseNumber(range, min, max, expression);
                to = slash >= 0 ? max : from;
            }

            for (var v = from; v <= to; v += step)
            {
                allowed[v] = true;
            }
        }

        return allowed;
    }

    private static int ParseNumber(string text, int min, int max, string expression)
    {
        var value = text.Length is > 0 and <= 2 && text.All(char.IsAsciiDigit) ? int.Parse(text, System.Globalization.CultureInfo.InvariantCulture) : -1;
        if (value < min || value > max)
        {
            throw new FormatException($"Cron 값 '{text}'은(는) {min}~{max} 사이의 숫자여야 합니다 ('{expression}')");
        }

        return value;
    }
}
