using Portal.Data;

namespace Portal.Framework.Web.Logging;

/// <summary>개인정보 접속기록의 행위 코드 (<c>LOG.TB_PII_ACCESS_LOG.ACTION_CD</c>).</summary>
public static class PiiActions
{
    /// <summary>화면 조회.</summary>
    public const string View = "VIEW";

    /// <summary>파일 다운로드 (사유 필수).</summary>
    public const string Download = "DOWNLOAD";

    /// <summary>마스킹 해제 (사유 필수).</summary>
    public const string Unmask = "UNMASK";
}

/// <summary>
/// 개인정보 접속기록 (10년 보관, 해시 체인). 개인정보가 담긴 목록을 내려주거나 마스킹을 풀기 전에 Service가 부른다.
/// 기록에 실패하면 예외가 나므로 개인정보도 나가지 않는다. 사용자·IP는 SP 공통 파라미터로 자동으로 들어간다.
/// </summary>
public interface IPiiAccessLog
{
    /// <summary>한 건 기록. <paramref name="targetDesc"/>에는 건수·조건만 넣고 개인정보 원문(이름, 검색어 등)은 넣지 않는다.</summary>
    /// <exception cref="Portal.Core.Errors.BizException">다운로드·마스킹 해제인데 사유가 없으면 <see cref="Portal.Core.Keys.MessageKeys.PiiReasonRequired"/>.</exception>
    Task WriteAsync(string programId, string actionCd, string? targetDesc, string? reason, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class PiiAccessLog(ISpExecutor sp) : IPiiAccessLog
{
    internal const string WriteSp = "LOG.USP_PII_ACCESS_LOG_WRITE";

    /// <summary>대상·사유 최대 길이 (표 컬럼과 같다).</summary>
    public const int MaxLength = 500;

    /// <inheritdoc />
    public Task WriteAsync(string programId, string actionCd, string? targetDesc, string? reason, CancellationToken cancellationToken = default) =>
        sp.ExecuteAsync(
            WriteSp,
            new
            {
                P_PROGRAM_ID = programId,
                P_ACTION_CD = actionCd,
                P_TARGET_DESC = Cut(targetDesc),
                P_REASON = Cut(reason?.Trim()),
            },
            cancellationToken);

    private static string? Cut(string? value) => value is { Length: > MaxLength } ? value[..MaxLength] : value;
}
