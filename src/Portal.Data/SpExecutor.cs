using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portal.Core.Context;
using Portal.Core.Results;
using Portal.Data.Internal;

namespace Portal.Data;

/// <summary>
/// <see cref="ISpExecutor"/>의 SQL Server 구현. 요청(Scope)마다 하나씩 만들어지며 스레드 간에 공유하지 않는다.
/// Scope가 끝날 때 커밋되지 않은 트랜잭션이 남아 있으면 롤백하고 연결을 닫는다.
/// </summary>
public sealed class SpExecutor(
    ISqlConnectionFactory connectionFactory,
    ICurrentUser currentUser,
    IOptions<SpExecutorOptions> options,
    TimeProvider timeProvider,
    ILogger<SpExecutor> logger) : ISpExecutor, IAsyncDisposable
{
    private readonly SpExecutorOptions _options = options.Value;
    private SpTransaction? _transaction;

    /// <inheritdoc />
    public Task<IReadOnlyList<T>> QueryAsync<T>(string spName, object? parameters = null, CancellationToken cancellationToken = default) =>
        RunReaderAsync(spName, parameters, (r, ct) => ReadListAsync<T>(r, ct), cancellationToken);

    /// <inheritdoc />
    public Task<T?> QuerySingleAsync<T>(string spName, object? parameters = null, CancellationToken cancellationToken = default) =>
        RunReaderAsync(spName, parameters, (r, ct) => ReadSingleAsync<T>(r, ct), cancellationToken);

    /// <inheritdoc />
    public async Task<PageResult<T>> QueryPageAsync<T>(string spName, object? parameters = null, CancellationToken cancellationToken = default)
    {
        var built = SpParameterBuilder.Build(parameters, currentUser);
        var pageNo = RequireInt(built, "P_PAGE_NO");
        var pageSize = RequireInt(built, "P_PAGE_SIZE");

        return await RunReaderAsync(spName, built, async (reader, ct) =>
        {
            var items = await ReadListAsync<T>(reader, ct).ConfigureAwait(false);
            if (!await reader.NextResultAsync(ct).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"{spName}: 목록 SP는 두 번째 결과셋(TOTAL_CNT)을 돌려줘야 합니다.");
            }

            var total = await ReadSingleAsync<int>(reader, ct).ConfigureAwait(false);
            return new PageResult<T> { Items = items, TotalCount = total, PageNo = pageNo, PageSize = pageSize };
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<TResult> QueryMultipleAsync<TResult>(string spName, object? parameters, Func<ISpResultReader, Task<TResult>> read, CancellationToken cancellationToken = default) =>
        RunReaderAsync(spName, parameters, (r, _) => read(new ResultReader(r)), cancellationToken);

    /// <inheritdoc />
    public Task<SaveResult> ExecuteAsync(string spName, object? parameters = null, CancellationToken cancellationToken = default) =>
        RunReaderAsync(spName, parameters, async (reader, ct) =>
        {
            if (reader.FieldCount == 0 || !await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                return SaveResult.Empty;
            }

            var affected = 0;
            string? newKey = null;
            for (var i = 0; i < reader.FieldCount; i++)
            {
                switch (NameRules.Normalize(reader.GetName(i)))
                {
                    case "AFFECTEDCNT":
                        affected = (int)ValueConverter.FromDb(reader.GetValue(i), typeof(int))!;
                        break;
                    case "NEWKEY":
                        newKey = (string?)ValueConverter.FromDb(reader.GetValue(i), typeof(string));
                        break;
                }
            }

            return new SaveResult(affected, newKey);
        }, cancellationToken);

    /// <inheritdoc />
    public async Task<ISpTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("이미 트랜잭션이 진행 중입니다. 트랜잭션은 중첩할 수 없습니다.");
        }

        var connection = connectionFactory.Create();
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            _transaction = new SpTransaction(this, connection, tx);
            return _transaction;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_transaction is { } tx)
        {
            await tx.DisposeAsync().ConfigureAwait(false);
        }
    }

    private Task<TResult> RunReaderAsync<TResult>(string spName, object? parameters, Func<DbDataReader, CancellationToken, Task<TResult>> read, CancellationToken cancellationToken) =>
        RunReaderAsync(spName, SpParameterBuilder.Build(parameters, currentUser), read, cancellationToken);

    private async Task<TResult> RunReaderAsync<TResult>(string spName, IReadOnlyList<KeyValuePair<string, object?>> parameters, Func<DbDataReader, CancellationToken, Task<TResult>> read, CancellationToken cancellationToken)
    {
        NameRules.EnsureSpName(spName);

        var owned = _transaction is null ? connectionFactory.Create() : null;
        var connection = owned ?? _transaction!.Connection;
        var started = timeProvider.GetTimestamp();
        try
        {
            if (owned is not null)
            {
                await owned.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var command = connection.CreateCommand();
            command.CommandType = CommandType.StoredProcedure;
#pragma warning disable CA2100 // SP 이름은 NameRules.EnsureSpName으로 형식을 검증한 값만 쓴다
            command.CommandText = spName;
#pragma warning restore CA2100
            command.CommandTimeout = _options.CommandTimeoutSeconds;
            command.Transaction = _transaction?.Transaction;
            foreach (var (name, value) in parameters)
            {
                command.Parameters.Add(SqlParameterFactory.Create(name, value));
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var result = await read(reader, cancellationToken).ConfigureAwait(false);

            // 남은 결과셋을 끝까지 읽어야 뒤쪽에서 발생한 THROW가 예외로 올라온다.
            while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
            {
            }

            return result;
        }
        catch (SqlException ex) when (SpErrorTranslator.IsBizError(ex.Number))
        {
            throw SpErrorTranslator.ToBizException(ex.Number, ex.Message, ex);
        }
        finally
        {
            var elapsed = timeProvider.GetElapsedTime(started);
            if (elapsed.TotalMilliseconds > _options.SlowThresholdMs)
            {
                logger.LogWarning("Slow SP {SpName} took {ElapsedMs} ms", spName, (long)elapsed.TotalMilliseconds);
            }

            if (owned is not null)
            {
                await owned.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<IReadOnlyList<T>> ReadListAsync<T>(DbDataReader reader, CancellationToken cancellationToken)
    {
        var list = new List<T>();
        if (reader.FieldCount == 0)
        {
            return list;
        }

        var map = RowMapper.For<T>(reader);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(map(reader));
        }

        return list;
    }

    private static async Task<T?> ReadSingleAsync<T>(DbDataReader reader, CancellationToken cancellationToken)
    {
        if (reader.FieldCount == 0 || !await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return default;
        }

        var item = RowMapper.For<T>(reader)(reader);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
        }

        return item;
    }

    private static int RequireInt(IReadOnlyList<KeyValuePair<string, object?>> parameters, string name)
    {
        foreach (var (key, value) in parameters)
        {
            if (key == name && value is not null)
            {
                return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        throw new ArgumentException($"목록 조회에는 {name} 파라미터가 필요합니다.", nameof(parameters));
    }

    private sealed class ResultReader(DbDataReader reader) : ISpResultReader
    {
        private bool _started;

        public async Task<IReadOnlyList<T>> ReadAsync<T>(CancellationToken cancellationToken = default)
        {
            await MoveAsync(cancellationToken).ConfigureAwait(false);
            return await ReadListAsync<T>(reader, cancellationToken).ConfigureAwait(false);
        }

        public async Task<T?> ReadSingleAsync<T>(CancellationToken cancellationToken = default)
        {
            await MoveAsync(cancellationToken).ConfigureAwait(false);
            return await SpExecutor.ReadSingleAsync<T>(reader, cancellationToken).ConfigureAwait(false);
        }

        private async Task MoveAsync(CancellationToken cancellationToken)
        {
            if (!_started)
            {
                _started = true;
                return;
            }

            if (!await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("SP가 돌려준 결과셋보다 많이 읽으려고 했습니다.");
            }
        }
    }

    private sealed class SpTransaction(SpExecutor owner, SqlConnection connection, SqlTransaction transaction) : ISpTransaction
    {
        private bool _completed;

        public SqlConnection Connection { get; } = connection;

        public SqlTransaction Transaction { get; } = transaction;

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _completed = true;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_completed)
                {
                    await Transaction.RollbackAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                owner._transaction = null;
                await Transaction.DisposeAsync().ConfigureAwait(false);
                await Connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
