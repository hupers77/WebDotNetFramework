namespace Portal.Infrastructure.Files;

/// <summary>첨부 묶음 (<c>FW.USP_FILE_GET_LIST</c> 결과셋 1).</summary>
public sealed class FileGroupDto
{
    /// <summary>묶음 ID. 업무 데이터에 이 값을 저장한다.</summary>
    public long FileGroupId { get; set; }

    /// <summary>권한 기준 프로그램.</summary>
    public string ProgramId { get; set; } = "";

    /// <summary>올린 사용자의 회사.</summary>
    public string CompanyCd { get; set; } = "";

    /// <summary>Y면 다른 회사 사용자도 받을 수 있다.</summary>
    public string ShareYn { get; set; } = "N";

    /// <summary>업무 저장으로 확정됐는지 (N이면 임시).</summary>
    public string ConfirmYn { get; set; } = "N";
}

/// <summary>첨부 파일 한 건 (<c>FW.USP_FILE_GET_LIST</c> 결과셋 2). 저장 경로는 내보내지 않는다.</summary>
public sealed class FileItemDto
{
    /// <summary>파일 ID (다운로드·삭제에 쓴다).</summary>
    public long FileId { get; set; }

    /// <summary>원래 파일 이름.</summary>
    public string OrgFileNm { get; set; } = "";

    /// <summary>확장자 (소문자, 점 없이).</summary>
    public string FileExt { get; set; } = "";

    /// <summary>크기(바이트).</summary>
    public long FileSize { get; set; }

    /// <summary>정렬 순서.</summary>
    public int SortNo { get; set; }

    /// <summary>다운로드 횟수.</summary>
    public int DownCnt { get; set; }

    /// <summary>올린 사용자.</summary>
    public string RegId { get; set; } = "";

    /// <summary>올린 일시 (UTC).</summary>
    public DateTime RegDt
    {
        get;
        set => field = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
}

/// <summary>첨부 목록.</summary>
/// <param name="Group">묶음 정보.</param>
/// <param name="Files">삭제되지 않은 파일.</param>
public sealed record FileListResult(FileGroupDto Group, IReadOnlyList<FileItemDto> Files);

/// <summary>첨부 파일 한 건의 저장 정보 (<c>FW.USP_FILE_GET</c>). 저장 경로가 있으므로 화면 응답으로 내보내지 않는다.</summary>
public sealed class StoredFileDto
{
    /// <summary>파일 ID.</summary>
    public long FileId { get; set; }

    /// <summary>묶음 ID.</summary>
    public long FileGroupId { get; set; }

    /// <summary>권한 기준 프로그램.</summary>
    public string ProgramId { get; set; } = "";

    /// <summary>묶음 회사.</summary>
    public string CompanyCd { get; set; } = "";

    /// <summary>공유 여부.</summary>
    public string ShareYn { get; set; } = "N";

    /// <summary>원래 파일 이름.</summary>
    public string OrgFileNm { get; set; } = "";

    /// <summary>확장자.</summary>
    public string FileExt { get; set; } = "";

    /// <summary>크기(바이트).</summary>
    public long FileSize { get; set; }

    /// <summary>저장소 상대 경로.</summary>
    public string StoredPath { get; set; } = "";

    /// <summary>SHA-256.</summary>
    public string ContentHash { get; set; } = "";

    /// <summary>올린 사용자.</summary>
    public string RegId { get; set; } = "";
}

/// <summary>업로드 결과 (<c>FW.USP_FILE_SAVE</c>).</summary>
public sealed class FileUploadResult
{
    /// <summary>묶음 ID. 새 묶음이면 새로 만든 값이다.</summary>
    public long FileGroupId { get; set; }

    /// <summary>등록된 파일 ID.</summary>
    public long FileId { get; set; }
}

/// <summary>첨부 오류 메시지 키.</summary>
public static class FileMessageKeys
{
    /// <summary>허용되지 않는 파일 형식입니다.</summary>
    public const string ExtDenied = "MSG.FILE_EXT_DENIED";

    /// <summary>파일 크기는 {0}MB 이하여야 합니다.</summary>
    public const string TooLarge = "MSG.FILE_TOO_LARGE";

    /// <summary>첨부할 수 있는 파일 수를 넘었습니다.</summary>
    public const string CountExceeded = "MSG.FILE_COUNT_EXCEEDED";

    /// <summary>파일 내용이 확장자와 맞지 않습니다.</summary>
    public const string ContentMismatch = "MSG.FILE_CONTENT_MISMATCH";

    /// <summary>빈 파일은 올릴 수 없습니다.</summary>
    public const string Empty = "MSG.FILE_EMPTY";
}
