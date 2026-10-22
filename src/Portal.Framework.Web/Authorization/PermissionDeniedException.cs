namespace Portal.Framework.Web.Authorization;

/// <summary>
/// Action 안에서 추가로 확인한 기능 권한이 없을 때 (예: 그리드 저장의 추가·삭제 행). 공통 오류 처리가 403으로 바꾼다.
/// </summary>
/// <param name="programId">프로그램 ID.</param>
/// <param name="funcCd">없는 기능 코드.</param>
public sealed class PermissionDeniedException(string programId, string funcCd)
    : Exception($"{programId}:{funcCd} 권한이 없습니다.")
{
    /// <summary>프로그램 ID.</summary>
    public string ProgramId { get; } = programId;

    /// <summary>없는 기능 코드.</summary>
    public string FuncCd { get; } = funcCd;
}
