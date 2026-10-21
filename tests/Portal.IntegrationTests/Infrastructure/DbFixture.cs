using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portal.Data;
using Portal.Testing;

namespace Portal.IntegrationTests.Infrastructure;

/// <summary>
/// 테스트 DB 연결. 테스트마다 <see cref="BeginAsync"/>로 트랜잭션을 열고, 끝나면 롤백해 서로 격리한다(개발 표준 가이드 9장).
/// </summary>
public sealed class DbFixture
{
    private const string DefaultConnectionString =
        "Server=localhost;Database=PORTAL_LOCAL;Integrated Security=True;Encrypt=True;TrustServerCertificate=True";

    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("PORTAL_TEST_DB") is { Length: > 0 } cs ? cs : DefaultConnectionString;

    /// <summary>롤백되는 트랜잭션을 연다. 기본 사용자는 시스템 관리자 역할을 가진 테스트 사용자다.</summary>
    public async Task<TestDb> BeginAsync(FakeCurrentUser? user = null)
    {
        var currentUser = user ?? new FakeCurrentUser { UserId = "it.admin" };
        var sp = new SpExecutor(
            new SqlConnectionFactory(ConnectionString),
            currentUser,
            Options.Create(new SpExecutorOptions()),
            TimeProvider.System,
            NullLogger<SpExecutor>.Instance);
        var tx = await sp.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var db = new TestDb(sp, tx, currentUser);
        await db.SeedUserAsync(currentUser.UserId!, "HQ", "EMP", roles: ["SYS_ADMIN"]);
        return db;
    }
}

/// <summary>테스트 하나의 DB 범위. 끝나면(Dispose) 모든 변경을 롤백한다.</summary>
public sealed class TestDb(SpExecutor sp, ISpTransaction transaction, FakeCurrentUser user) : IAsyncDisposable
{
    /// <summary>테스트 대상 SP 실행기 (이 트랜잭션 안에서 실행).</summary>
    public ISpExecutor Sp => sp;

    /// <summary>SP 공통 파라미터로 들어가는 현재 사용자. 바꾸면 다음 호출부터 적용된다.</summary>
    public FakeCurrentUser User { get; } = user;

    /// <summary>현재 사용자를 바꾼다 (협력사 사용자 시나리오 등).</summary>
    public void AsUser(string userId, string companyCd, string userTypeCd)
    {
        User.UserId = userId;
        User.CompanyCd = companyCd;
        User.UserTypeCd = userTypeCd;
    }

    /// <summary>테스트 데이터 준비·확인용 SQL. 테스트 코드에서만 쓰며 애플리케이션 코드에서는 금지다.</summary>
    public async Task<int> ExecAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var cmd = CreateCommand(sql, parameters);
        return await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>스칼라 조회.</summary>
    public async Task<T> ScalarAsync<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var cmd = CreateCommand(sql, parameters);
        var value = await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return value is null or DBNull ? default! : (T)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>여러 행 조회 (첫 컬럼).</summary>
    public async Task<List<T>> ColumnAsync<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var cmd = CreateCommand(sql, parameters);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var list = new List<T>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            list.Add(reader.IsDBNull(0) ? default! : (T)reader.GetValue(0));
        }

        return list;
    }

    /// <summary>테스트 사용자를 만든다.</summary>
    public async Task SeedUserAsync(string userId, string companyCd, string userTypeCd, string? deptId = null, params string[] roles)
    {
        await ExecAsync(
            "INSERT INTO FW.TB_USER (USER_ID, COMPANY_CD, DEPT_ID, USER_NM, USER_TYPE_CD) VALUES (@U, @C, @D, @U, @T)",
            ("@U", userId), ("@C", companyCd), ("@D", deptId), ("@T", userTypeCd));
        foreach (var role in roles)
        {
            await ExecAsync("INSERT INTO FW.TB_USER_ROLE (USER_ID, ROLE_ID) VALUES (@U, @R)", ("@U", userId), ("@R", role));
        }
    }

    /// <summary>협력사를 만든다.</summary>
    public Task SeedCompanyAsync(string companyCd, string type = "PARTNER") =>
        ExecAsync("INSERT INTO FW.TB_COMPANY (COMPANY_CD, COMPANY_NM, COMPANY_TYPE_CD) VALUES (@C, @C, @T)", ("@C", companyCd), ("@T", type));

    /// <summary>부서를 만든다.</summary>
    public Task SeedDeptAsync(string deptId, string companyCd, string? parentDeptId = null) =>
        ExecAsync("INSERT INTO FW.TB_DEPT (DEPT_ID, COMPANY_CD, PARENT_DEPT_ID, DEPT_NM) VALUES (@D, @C, @P, @D)", ("@D", deptId), ("@C", companyCd), ("@P", parentDeptId));

    public ValueTask DisposeAsync() => transaction.DisposeAsync();

    private SqlCommand CreateCommand(string sql, (string Name, object? Value)[] parameters)
    {
        var (connection, tx) = sp.CurrentTransaction
            ?? throw new InvalidOperationException("트랜잭션이 이미 끝났습니다 (SP 오류로 서버가 롤백했을 수 있음).");
        var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
#pragma warning disable CA2100 // 테스트 데이터 준비용 고정 SQL
        cmd.CommandText = sql;
#pragma warning restore CA2100
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return cmd;
    }
}

/// <summary>모든 통합 테스트가 공유하는 DB 픽스처 컬렉션.</summary>
[CollectionDefinition(Name)]
public sealed class DbTests : ICollectionFixture<DbFixture>
{
    public const string Name = "db";
}
