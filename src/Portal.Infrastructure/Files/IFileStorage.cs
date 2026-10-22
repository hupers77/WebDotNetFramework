namespace Portal.Infrastructure.Files;

/// <summary>
/// 첨부 내용 저장소. 경로는 <see cref="FileService"/>가 만든 상대 경로(<c>yyyy/MM/&lt;guid&gt;</c>)만 받고,
/// 루트 밖을 가리키면 거부한다. 업무 코드는 이 인터페이스를 직접 쓰지 않는다.
/// </summary>
public interface IFileStorage
{
    /// <summary>내용을 새 파일로 쓴다. 같은 경로가 있으면 실패한다.</summary>
    Task WriteAsync(string storedPath, Stream content, CancellationToken cancellationToken = default);

    /// <summary>읽기 스트림. 파일이 없으면 <see cref="FileNotFoundException"/>.</summary>
    Stream OpenRead(string storedPath);

    /// <summary>지운다. 없으면 아무것도 하지 않는다.</summary>
    void Delete(string storedPath);
}
