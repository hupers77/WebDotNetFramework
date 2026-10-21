using System.Reflection;
using Portal.Core.Results;
using Portal.Data;

namespace Portal.Testing;

/// <summary>
/// DB 없이 Service를 테스트하기 위한 <see cref="ISpExecutor"/> 대역.
/// SP 이름별로 돌려줄 결과를 등록하고, 실행된 호출과 파라미터를 <see cref="Calls"/>로 확인한다.
/// </summary>
/// <example>
/// <code>
/// var sp = new FakeSpExecutor()
///     .Returns("FW.USP_CODE_GET_LIST", new PageResult&lt;CodeDto&gt; { ... })
///     .Throws("FW.USP_CODE_SAVE", new BizException(MessageKeys.DupCode));
/// </code>
/// </example>
public sealed class FakeSpExecutor : ISpExecutor
{
    private readonly Dictionary<string, Func<IReadOnlyDictionary<string, object?>, object?>> _results = new(StringComparer.Ordinal);

    private readonly List<SpCall> _calls = [];

    /// <summary>실행된 SP 호출 순서대로 (웹 테스트처럼 여러 요청이 동시에 불러도 되도록 복사본을 준다).</summary>
    public IReadOnlyList<SpCall> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    /// <summary>커밋된 트랜잭션 수.</summary>
    public int CommitCount { get; private set; }

    /// <summary>
    /// SP 결과를 등록한다. 값은 메서드에 맞는 형식이어야 한다:
    /// QueryAsync → <c>IEnumerable&lt;T&gt;</c>, QuerySingleAsync → <c>T</c>, QueryPageAsync → <c>PageResult&lt;T&gt;</c>,
    /// ExecuteAsync → <see cref="SaveResult"/>, QueryMultipleAsync → <c>object[]</c>(결과셋별 목록 또는 단건).
    /// </summary>
    public FakeSpExecutor Returns(string spName, object? result) => Returns(spName, _ => result);

    /// <summary>파라미터에 따라 결과를 만든다.</summary>
    public FakeSpExecutor Returns(string spName, Func<IReadOnlyDictionary<string, object?>, object?> factory)
    {
        lock (_calls)
        {
            _results[spName] = factory;
        }

        return this;
    }

    /// <summary>SP가 예외를 던지게 한다 (예: <c>BizException(MessageKeys.DupCode)</c>).</summary>
    public FakeSpExecutor Throws(string spName, Exception exception) => Returns(spName, _ => throw exception);

    /// <summary>마지막으로 호출된 해당 SP의 호출 정보.</summary>
    public SpCall LastCall(string spName) =>
        Calls.LastOrDefault(c => c.SpName == spName) ?? throw new InvalidOperationException($"{spName}이 호출되지 않았습니다.");

    public Task<IReadOnlyList<T>> QueryAsync<T>(string spName, object? parameters = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<T>>(Invoke(spName, parameters) switch
        {
            null => [],
            IEnumerable<T> list => list.ToList(),
            var other => throw WrongType<IEnumerable<T>>(spName, other),
        });

    public Task<T?> QuerySingleAsync<T>(string spName, object? parameters = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Invoke(spName, parameters) switch
        {
            null => default,
            T item => item,
            var other => throw WrongType<T>(spName, other),
        });

    public Task<PageResult<T>> QueryPageAsync<T>(string spName, object? parameters = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Invoke(spName, parameters) switch
        {
            null => new PageResult<T> { Items = [], TotalCount = 0, PageNo = 1, PageSize = 0 },
            PageResult<T> page => page,
            var other => throw WrongType<PageResult<T>>(spName, other),
        });

    public Task<TResult> QueryMultipleAsync<TResult>(string spName, object? parameters, Func<ISpResultReader, Task<TResult>> read, CancellationToken cancellationToken = default) =>
        Invoke(spName, parameters) switch
        {
            object[] sets => read(new FakeResultReader(sets)),
            null => read(new FakeResultReader([])),
            var other => throw WrongType<object[]>(spName, other),
        };

    public Task<SaveResult> ExecuteAsync(string spName, object? parameters = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Invoke(spName, parameters) switch
        {
            null => new SaveResult(1, null),
            SaveResult result => result,
            var other => throw WrongType<SaveResult>(spName, other),
        });

    public Task<ISpTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<ISpTransaction>(new FakeTransaction(this));

    private object? Invoke(string spName, object? parameters)
    {
        var values = ToDictionary(parameters);
        Func<IReadOnlyDictionary<string, object?>, object?>? factory;
        lock (_calls)
        {
            _calls.Add(new SpCall(spName, values));
            _results.TryGetValue(spName, out factory);
        }

        return factory?.Invoke(values);
    }

    private static Dictionary<string, object?> ToDictionary(object? parameters)
    {
        if (parameters is null)
        {
            return [];
        }

        if (parameters is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            return pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        }

        return parameters.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => p.Name, p => p.GetValue(parameters), StringComparer.Ordinal);
    }

    private static InvalidOperationException WrongType<T>(string spName, object result) =>
        new($"{spName}: 등록된 결과 형식 {result.GetType().Name}을 {typeof(T).Name}(으)로 쓸 수 없습니다.");

    private sealed class FakeResultReader(object[] sets) : ISpResultReader
    {
        private int _index;

        public Task<IReadOnlyList<T>> ReadAsync<T>(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>(Next() switch
            {
                null => [],
                IEnumerable<T> list => list.ToList(),
                T single => [single],
                var other => throw new InvalidOperationException($"결과셋 형식 {other.GetType().Name}을 {typeof(T).Name} 목록으로 쓸 수 없습니다."),
            });

        public Task<T?> ReadSingleAsync<T>(CancellationToken cancellationToken = default) =>
            Task.FromResult(Next() switch
            {
                null => default,
                T single => single,
                IEnumerable<T> list => list.FirstOrDefault(),
                var other => throw new InvalidOperationException($"결과셋 형식 {other.GetType().Name}을 {typeof(T).Name}(으)로 쓸 수 없습니다."),
            });

        private object? Next() =>
            _index < sets.Length ? sets[_index++] : throw new InvalidOperationException("등록된 결과셋보다 많이 읽으려고 했습니다.");
    }

    private sealed class FakeTransaction(FakeSpExecutor owner) : ISpTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            owner.CommitCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>FakeSpExecutor가 기록한 SP 호출.</summary>
/// <param name="SpName">SP 이름.</param>
/// <param name="Parameters">호출자가 넘긴 파라미터(공통 파라미터 제외).</param>
public sealed record SpCall(string SpName, IReadOnlyDictionary<string, object?> Parameters);
