namespace Portal.Infrastructure.Files;

/// <summary>
/// 첨부 저장소 설정 (<c>appsettings.json</c>의 <c>Portal:File</c>). 서버마다 경로가 다르므로 DB 설정이 아닌 파일에 둔다.
/// 크기·개수·확장자 정책은 DB 설정(<c>FILE.MAX_SIZE_MB</c>, <c>FILE.MAX_COUNT</c>)과 공통코드 <c>FILE_EXT</c>가 정한다.
/// </summary>
public sealed class FileStorageOptions
{
    /// <summary>설정 섹션 이름.</summary>
    public const string SectionName = "Portal:File";

    /// <summary>
    /// 저장소 루트 폴더. 웹 루트 밖, 실행 권한 없는 폴더여야 하고 백업 대상이다(설계서 9장·16장).
    /// 비우면 콘텐츠 루트의 <c>App_Data/files</c>를 쓴다(개발 PC).
    /// </summary>
    public string StorageRoot { get; set; } = "";
}
