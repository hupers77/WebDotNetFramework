using Portal.Core.Results;

namespace Portal.Data;

/// <summary>
/// 저장 프로시저 실행기. 애플리케이션의 모든 DB 접근은 이 인터페이스로만 한다(설계서 8장).
/// </summary>
/// <remarks>
/// <para>파라미터는 익명 객체로 넘기고 속성 이름은 SP 파라미터 이름에서 <c>@</c>를 뺀 것(<c>P_GROUP_CD</c>)으로 쓴다.</para>
/// <para>공통 파라미터 <c>@P_USER_ID</c>, <c>@P_LANG_CD</c>, <c>@P_CLIENT_IP</c>는 현재 사용자에서 자동으로 채우며,
/// 호출자가 같은 이름을 넘겨도 무시된다.</para>
/// <para>결과 컬럼은 대문자 스네이크(<c>CODE_NM</c>)를 PascalCase 속성(<c>CodeNm</c>)에 매핑한다.</para>
/// </remarks>
public interface ISpExecutor
{
    /// <summary>첫 번째 결과셋을 목록으로 읽는다.</summary>
    Task<IReadOnlyList<T>> QueryAsync<T>(string spName, object? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>첫 번째 결과셋의 첫 행을 읽는다. 행이 없으면 기본값.</summary>
    Task<T?> QuerySingleAsync<T>(string spName, object? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 목록 SP(<c>_GET_LIST</c>)를 실행한다. 결과셋 1은 목록, 결과셋 2는 <c>TOTAL_CNT</c> 한 행이어야 한다.
    /// 페이지 번호·크기는 파라미터의 <c>P_PAGE_NO</c>, <c>P_PAGE_SIZE</c>에서 읽는다.
    /// </summary>
    Task<PageResult<T>> QueryPageAsync<T>(string spName, object? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 결과셋이 여러 개인 SP(<c>_GET</c> 상세 + 하위 목록 등)를 실행하고, 결과셋을 순서대로 읽는다.
    /// </summary>
    Task<TResult> QueryMultipleAsync<TResult>(string spName, object? parameters, Func<ISpResultReader, Task<TResult>> read, CancellationToken cancellationToken = default);

    /// <summary>
    /// 저장·삭제·처리 SP(<c>_SAVE</c>, <c>_DEL</c>, <c>_EXEC</c>)를 실행한다.
    /// SP가 <c>AFFECTED_CNT</c>, <c>NEW_KEY</c> 컬럼을 가진 결과 한 행을 돌려주면 그 값을 담는다.
    /// </summary>
    Task<SaveResult> ExecuteAsync(string spName, object? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 여러 SP를 한 트랜잭션으로 묶는다. 반환된 범위가 끝날 때까지 이 실행기의 호출은 같은 연결·트랜잭션을 쓰며,
    /// <see cref="ISpTransaction.CommitAsync"/>를 부르지 않고 범위를 끝내면 롤백된다.
    /// 단일 SP의 트랜잭션은 SP 내부에서 처리하므로 이 메서드가 필요 없다.
    /// </summary>
    Task<ISpTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

/// <summary>여러 결과셋을 순서대로 읽는 리더.</summary>
public interface ISpResultReader
{
    /// <summary>다음 결과셋을 목록으로 읽는다.</summary>
    Task<IReadOnlyList<T>> ReadAsync<T>(CancellationToken cancellationToken = default);

    /// <summary>다음 결과셋의 첫 행을 읽는다. 행이 없으면 기본값.</summary>
    Task<T?> ReadSingleAsync<T>(CancellationToken cancellationToken = default);
}

/// <summary>애플리케이션 트랜잭션 범위.</summary>
public interface ISpTransaction : IAsyncDisposable
{
    /// <summary>커밋한다.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
}

/// <summary>저장 계열 SP의 결과.</summary>
/// <param name="AffectedCnt">처리 건수.</param>
/// <param name="NewKey">신규 키(등록 시). 없으면 null.</param>
public sealed record SaveResult(int AffectedCnt, string? NewKey)
{
    /// <summary>결과 행을 돌려주지 않은 SP의 결과.</summary>
    public static SaveResult Empty { get; } = new(0, null);
}
