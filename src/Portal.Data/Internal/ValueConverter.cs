using System.Globalization;

namespace Portal.Data.Internal;

/// <summary>DB 값과 .NET 형식 사이의 변환.</summary>
internal static class ValueConverter
{
    public static object? FromDb(object? value, Type targetType)
    {
        if (value is null || value is DBNull)
        {
            return targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null
                ? Activator.CreateInstance(targetType)
                : null;
        }

        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (type.IsInstanceOfType(value))
        {
            return value;
        }

        if (type.IsEnum)
        {
            return value is string s
                ? Enum.Parse(type, s, ignoreCase: true)
                : Enum.ToObject(type, value);
        }

        return value switch
        {
            DateTime dt when type == typeof(DateOnly) => DateOnly.FromDateTime(dt),
            DateTime dt when type == typeof(DateTimeOffset) => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            TimeSpan ts when type == typeof(TimeOnly) => TimeOnly.FromTimeSpan(ts),
            string s when type == typeof(Guid) => Guid.Parse(s),
            string s when type == typeof(char) && s.Length == 1 => s[0],
            _ => Convert.ChangeType(value, type, CultureInfo.InvariantCulture),
        };
    }

    public static bool IsScalar(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsPrimitive
            || t.IsEnum
            || t == typeof(string)
            || t == typeof(decimal)
            || t == typeof(DateTime)
            || t == typeof(DateTimeOffset)
            || t == typeof(DateOnly)
            || t == typeof(TimeOnly)
            || t == typeof(TimeSpan)
            || t == typeof(Guid)
            || t == typeof(byte[]);
    }
}
