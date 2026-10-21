namespace Portal.Core.Results;

/// <summary>서버 페이징 목록 결과. 목록 SP(<c>_GET_LIST</c>)의 결과셋 1(목록) + 결과셋 2(전체 건수)에 대응한다.</summary>
/// <typeparam name="T">행 형식.</typeparam>
public sealed class PageResult<T>
{
    /// <summary>현재 페이지의 행.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>조건에 맞는 전체 건수.</summary>
    public required int TotalCount { get; init; }

    /// <summary>현재 페이지 번호(1부터).</summary>
    public required int PageNo { get; init; }

    /// <summary>페이지 크기.</summary>
    public required int PageSize { get; init; }

    /// <summary>전체 페이지 수.</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
