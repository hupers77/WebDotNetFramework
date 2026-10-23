namespace Portal.Infrastructure.Pdf;

/// <summary>
/// HTML→PDF 변환 설정 (<c>appsettings.json</c>의 <c>Portal:Pdf</c>). 서버마다 브라우저 경로가 다르므로 DB 설정이 아닌 파일에 둔다(설계서 2장·11장).
/// </summary>
public sealed class PdfOptions
{
    /// <summary>설정 섹션 이름.</summary>
    public const string SectionName = "Portal:Pdf";

    /// <summary>Windows 서버의 Microsoft Edge 기본 설치 경로.</summary>
    public const string DefaultEdgePath = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";

    /// <summary>헤드리스로 띄울 브라우저 실행 파일(Edge 또는 Chromium). 비우면 <see cref="DefaultEdgePath"/>.</summary>
    public string BrowserPath { get; set; } = "";

    /// <summary>동시에 띄우는 브라우저 수(기본 4). 넘으면 앞 변환이 끝날 때까지 기다린다.</summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>변환 한 건의 제한 시간(초, 기본 60). 넘으면 브라우저를 끝내고 실패한다.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// 브라우저 샌드박스를 끈다(<c>--no-sandbox</c>). Linux 컨테이너에서 root로 테스트할 때만 쓰고, 운영(Windows)에서는 켜지 않는다.
    /// </summary>
    public bool NoSandbox { get; set; }
}
