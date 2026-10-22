namespace Portal.Web.Models;

/// <summary>첨부 삭제 요청 (첨부 부품, AJAX).</summary>
/// <param name="FileId">파일 ID.</param>
public sealed record FileDeleteRequest(long FileId);
