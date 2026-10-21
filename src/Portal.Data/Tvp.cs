using System.Data;
using System.Reflection;
using Portal.Data.Internal;

namespace Portal.Data;

/// <summary>
/// 테이블 반환 파라미터(TVP) 값. 그리드 일괄 저장처럼 여러 행을 한 번에 SP로 보낼 때 쓴다.
/// </summary>
/// <example>
/// <code>
/// await sp.ExecuteAsync("FW.USP_CODE_SAVE", new { P_GROUP_CD = groupCd, P_ROWS = Tvp.From("FW.TVP_CODE", rows) });
/// </code>
/// </example>
public sealed class Tvp
{
    private Tvp(string typeName, DataTable table)
    {
        TypeName = typeName;
        Table = table;
    }

    /// <summary>TVP 형식 이름 (예: <c>FW.TVP_CODE</c>).</summary>
    public string TypeName { get; }

    /// <summary>전달할 행.</summary>
    public DataTable Table { get; }

    /// <summary>
    /// DTO 목록으로 TVP를 만든다. TVP는 컬럼 순서로 매핑되므로, DTO 속성 선언 순서가 TVP 형식의 컬럼 순서와 같아야 한다.
    /// 순서를 맞추기 어려우면 컬럼 이름을 직접 지정하는 오버로드를 쓴다.
    /// </summary>
    public static Tvp From<T>(string typeName, IEnumerable<T> rows)
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetMethod is not null && p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.MetadataToken)
            .ToArray();
        return Build(typeName, rows, props);
    }

    /// <summary>
    /// TVP 컬럼 이름을 순서대로 지정해 만든다. 이름은 DTO 속성에 대문자 스네이크 규칙으로 매핑된다(<c>CODE_NM</c> → <c>CodeNm</c>).
    /// </summary>
    public static Tvp From<T>(string typeName, IEnumerable<T> rows, params string[] columns)
    {
        var byName = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => NameRules.Normalize(p.Name), StringComparer.Ordinal);
        var props = columns
            .Select(c => byName.TryGetValue(NameRules.Normalize(c), out var p)
                ? p
                : throw new ArgumentException($"{typeof(T).Name}에 TVP 컬럼 '{c}'에 해당하는 속성이 없습니다.", nameof(columns)))
            .ToArray();
        return Build(typeName, rows, props);
    }

    private static Tvp Build<T>(string typeName, IEnumerable<T> rows, PropertyInfo[] props)
    {
        NameRules.EnsureTvpName(typeName);

        var table = new DataTable { Locale = System.Globalization.CultureInfo.InvariantCulture };
        foreach (var prop in props)
        {
            var type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            if (type.IsEnum)
            {
                type = typeof(string);
            }

            table.Columns.Add(new DataColumn(NameRules.ToUpperSnake(prop.Name), type) { AllowDBNull = true });
        }

        foreach (var row in rows)
        {
            var values = new object[props.Length];
            for (var i = 0; i < props.Length; i++)
            {
                var value = props[i].GetValue(row);
                values[i] = value switch
                {
                    null => DBNull.Value,
                    Enum e => e.ToString(),
                    _ => value,
                };
            }

            table.Rows.Add(values);
        }

        return new Tvp(typeName, table);
    }
}
