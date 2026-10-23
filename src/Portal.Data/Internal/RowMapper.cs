using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;

namespace Portal.Data.Internal;

/// <summary>
/// 결과 행을 DTO로 매핑한다. 형식별 속성·생성자 정보는 한 번만 리플렉션으로 구해 캐시한다.
/// </summary>
/// <remarks>
/// 지원 형식: 스칼라(첫 컬럼), <see cref="SpRow"/>(모든 컬럼), 매개변수 없는 생성자 + 설정 가능한 속성, 위치 기반 record(생성자 매개변수 이름으로 매핑).
/// 결과에 없는 속성은 기본값으로 두고, DTO에 없는 컬럼은 무시한다.
/// </remarks>
internal static class RowMapper
{
    private static readonly ConcurrentDictionary<Type, TypeShape> Shapes = new();

    /// <summary>현재 결과셋의 컬럼 구성에 맞는 행 읽기 함수를 만든다.</summary>
    public static Func<DbDataReader, T> For<T>(DbDataReader reader)
    {
        var type = typeof(T);
        if (ValueConverter.IsScalar(type))
        {
            return r => (T)ValueConverter.FromDb(r.GetValue(0), type)!;
        }

        if (type == typeof(SpRow))
        {
            return (Func<DbDataReader, T>)(object)CreateSpRow(reader);
        }

        var shape = Shapes.GetOrAdd(type, TypeShape.Create);
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns.TryAdd(NameRules.Normalize(reader.GetName(i)), i);
        }

        return shape.Constructor is { } ctor
            ? CreateByConstructor<T>(ctor, shape.CtorParameters, columns)
            : CreateBySetters<T>(shape, columns);
    }

    /// <summary>모든 컬럼을 순서대로 담는다. <c>date</c> 컬럼은 <see cref="DateOnly"/>로 바꿔 날짜와 일시를 구분한다.</summary>
    private static Func<DbDataReader, SpRow> CreateSpRow(DbDataReader reader)
    {
        var names = new string[reader.FieldCount];
        var dates = new bool[reader.FieldCount];
        for (var i = 0; i < names.Length; i++)
        {
            names[i] = reader.GetName(i);
            dates[i] = string.Equals(reader.GetDataTypeName(i), "date", StringComparison.OrdinalIgnoreCase);
        }

        var shape = new SpRowShape(names);
        return r =>
        {
            var values = new object?[names.Length];
            for (var i = 0; i < values.Length; i++)
            {
                var value = r.GetValue(i);
                values[i] = value switch
                {
                    DBNull => null,
                    DateTime d when dates[i] => DateOnly.FromDateTime(d),
                    _ => value,
                };
            }

            return new SpRow(shape, values);
        };
    }

    private static Func<DbDataReader, T> CreateBySetters<T>(TypeShape shape, Dictionary<string, int> columns)
    {
        var bindings = shape.Setters
            .Where(s => columns.ContainsKey(s.Key))
            .Select(s => (Ordinal: columns[s.Key], s.Value.Type, s.Value.Set))
            .ToArray();
        var factory = shape.Factory!;

        return r =>
        {
            var item = factory();
            foreach (var (ordinal, type, set) in bindings)
            {
                set(item, ValueConverter.FromDb(r.GetValue(ordinal), type));
            }

            return (T)item;
        };
    }

    private static Func<DbDataReader, T> CreateByConstructor<T>(ConstructorInfo ctor, ParameterInfo[] parameters, Dictionary<string, int> columns)
    {
        var ordinals = parameters
            .Select(p => columns.TryGetValue(NameRules.Normalize(p.Name!), out var o) ? o : -1)
            .ToArray();

        return r =>
        {
            var args = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var value = ordinals[i] >= 0 ? r.GetValue(ordinals[i]) : null;
                args[i] = ValueConverter.FromDb(value, parameters[i].ParameterType);
            }

            return (T)ctor.Invoke(args);
        };
    }

    private sealed class TypeShape
    {
        public Func<object>? Factory { get; private init; }

        public Dictionary<string, (Type Type, Action<object, object?> Set)> Setters { get; private init; } = [];

        public ConstructorInfo? Constructor { get; private init; }

        public ParameterInfo[] CtorParameters { get; private init; } = [];

        public static TypeShape Create(Type type)
        {
            var defaultCtor = type.GetConstructor(Type.EmptyTypes);
            if (defaultCtor is null)
            {
                var ctor = type.GetConstructors()
                    .OrderByDescending(c => c.GetParameters().Length)
                    .FirstOrDefault()
                    ?? throw new InvalidOperationException($"{type.Name}: 매핑할 공개 생성자가 없습니다.");
                return new TypeShape { Constructor = ctor, CtorParameters = ctor.GetParameters() };
            }

            var setters = new Dictionary<string, (Type, Action<object, object?>)>(StringComparer.Ordinal);
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.SetMethod is { IsPublic: true } && prop.GetIndexParameters().Length == 0)
                {
                    setters.TryAdd(NameRules.Normalize(prop.Name), (prop.PropertyType, CompileSetter(type, prop)));
                }
            }

            var factory = Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(defaultCtor), typeof(object))).Compile();
            return new TypeShape { Factory = factory, Setters = setters };
        }

        private static Action<object, object?> CompileSetter(Type type, PropertyInfo prop)
        {
            var target = Expression.Parameter(typeof(object), "target");
            var value = Expression.Parameter(typeof(object), "value");
            var body = Expression.Call(
                Expression.Convert(target, type),
                prop.SetMethod!,
                Expression.Convert(value, prop.PropertyType));
            return Expression.Lambda<Action<object, object?>>(body, target, value).Compile();
        }
    }
}
