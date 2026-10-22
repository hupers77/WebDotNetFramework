using System.Collections.Concurrent;
using System.Reflection;
using Portal.Core.Context;

namespace Portal.Data.Internal;

/// <summary>호출자가 넘긴 파라미터 객체와 현재 사용자로 SP 파라미터 목록을 만든다.</summary>
internal static class SpParameterBuilder
{
    public const string UserId = "P_USER_ID";
    public const string LangCd = "P_LANG_CD";
    public const string ClientIp = "P_CLIENT_IP";

    private static readonly ConcurrentDictionary<Type, (string Name, Type Type, Func<object, object?> Get)[]> Getters = new();

    /// <summary>
    /// 파라미터 이름 → 값 목록. 공통 파라미터 3개는 항상 마지막에 현재 사용자 값으로 들어가고,
    /// 호출자가 같은 이름을 넘긴 값은 버린다(화면에서 보낸 사용자 ID로 다른 사람 행세를 못 하게).
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, object?>> Build(object? parameters, ICurrentUser user)
    {
        var result = new List<KeyValuePair<string, object?>>();
        foreach (var (name, value) in Enumerate(parameters))
        {
            NameRules.EnsureParameterName(name);
            if (name is UserId or LangCd or ClientIp)
            {
                continue;
            }

            if (result.Exists(p => p.Key == name))
            {
                throw new ArgumentException($"SP 파라미터가 중복되었습니다: '{name}'", nameof(parameters));
            }

            result.Add(new(name, value));
        }

        result.Add(new(UserId, user.UserId));
        result.Add(new(LangCd, user.LangCd));
        result.Add(new(ClientIp, user.ClientIp));
        return result;
    }

    private static IEnumerable<(string Name, object? Value)> Enumerate(object? parameters)
    {
        switch (parameters)
        {
            case null:
                yield break;
            case IEnumerable<KeyValuePair<string, object?>> pairs:
                foreach (var (k, v) in pairs)
                {
                    yield return (k, v);
                }

                yield break;
            default:
                var getters = Getters.GetOrAdd(parameters.GetType(), static t => t
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetMethod is not null && p.GetIndexParameters().Length == 0)
                    .Select(p => (p.Name, p.PropertyType, (Func<object, object?>)p.GetValue))
                    .ToArray());
                foreach (var (name, type, get) in getters)
                {
                    // 형식 없는 NULL은 nvarchar로 가서 BINARY 파라미터(ROW_VER 등)에 넣을 수 없으므로 바이너리 NULL로 보낸다
                    var value = get(parameters);
                    yield return (name, value is null && type == typeof(byte[]) ? NullBinary.Value : value);
                }

                yield break;
        }
    }
}

/// <summary>값이 null인 <c>byte[]</c> 파라미터. <see cref="SqlParameterFactory"/>가 VARBINARY NULL로 보낸다.</summary>
internal sealed class NullBinary
{
    public static readonly NullBinary Value = new();

    private NullBinary()
    {
    }
}
