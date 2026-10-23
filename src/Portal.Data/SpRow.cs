using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Portal.Data;

/// <summary>
/// 컬럼이 미리 정해지지 않은 결과 행 (레포트처럼 SP가 돌려주는 컬럼을 그대로 보여 줄 때). <c>QueryAsync&lt;SpRow&gt;</c>로 읽는다.
/// 컬럼 이름은 SP 결과 그대로(<c>LOG_DATE</c>, 대소문자 무시)이고 순서를 지킨다. DB의 NULL은 <see langword="null"/>,
/// <c>DATE</c> 컬럼은 <see cref="DateOnly"/>, <c>DATETIME2</c>는 <see cref="DateTime"/>(UTC)이다.
/// </summary>
/// <remarks>업무 화면은 DTO를 쓰고, 이 형식은 컬럼을 모르는 공통 기능(레포트)에만 쓴다.</remarks>
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "결과 행 하나를 뜻하는 이름이며, 사전 인터페이스는 컬럼 이름으로 읽기 위한 것이다.")]
public sealed class SpRow : IReadOnlyDictionary<string, object?>
{
    private readonly SpRowShape _shape;
    private readonly object?[] _values;

    /// <summary>테스트 등에서 행을 직접 만든다. 값 수는 컬럼 수와 같아야 한다.</summary>
    public SpRow(IReadOnlyList<string> columns, IReadOnlyList<object?> values)
        : this(new SpRowShape(columns), [.. values])
    {
    }

    internal SpRow(SpRowShape shape, object?[] values)
    {
        if (shape.Names.Count != values.Length)
        {
            throw new ArgumentException("값 수가 컬럼 수와 다릅니다.", nameof(values));
        }

        _shape = shape;
        _values = values;
    }

    /// <summary>컬럼 이름 (결과 순서).</summary>
    public IReadOnlyList<string> Columns => _shape.Names;

    /// <inheritdoc />
    public int Count => _values.Length;

    /// <inheritdoc />
    public IEnumerable<string> Keys => _shape.Names;

    /// <inheritdoc />
    public IEnumerable<object?> Values => _values;

    /// <summary>컬럼 이름으로 값을 읽는다. 없는 컬럼이면 <see cref="KeyNotFoundException"/>.</summary>
    public object? this[string key] =>
        _shape.Index.TryGetValue(key, out var i) ? _values[i] : throw new KeyNotFoundException($"결과에 '{key}' 컬럼이 없습니다.");

    /// <summary>순서(0부터)로 값을 읽는다.</summary>
    public object? this[int ordinal] => _values[ordinal];

    /// <inheritdoc />
    public bool ContainsKey(string key) => _shape.Index.ContainsKey(key);

    /// <inheritdoc />
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out object? value)
    {
        if (_shape.Index.TryGetValue(key, out var i))
        {
            value = _values[i];
            return true;
        }

        value = null;
        return false;
    }

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        for (var i = 0; i < _values.Length; i++)
        {
            yield return new(_shape.Names[i], _values[i]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>결과셋 하나의 컬럼 구성 (행마다 공유한다).</summary>
internal sealed class SpRowShape
{
    public SpRowShape(IReadOnlyList<string> names)
    {
        Names = names;
        Index = new Dictionary<string, int>(names.Count, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < names.Count; i++)
        {
            Index.TryAdd(names[i], i);
        }
    }

    public IReadOnlyList<string> Names { get; }

    public Dictionary<string, int> Index { get; }
}
