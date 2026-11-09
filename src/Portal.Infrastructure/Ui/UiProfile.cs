using Portal.Infrastructure.Messaging;

namespace Portal.Infrastructure.Ui;

/// <summary>
/// 설치할 때 고른 디자인 프로필 (<see cref="UiProfileLoader"/>가 기동할 때 한 번 읽는다). 화면은 이 값으로 로고·파비콘·색 변수 파일을 연결하고,
/// 레포트는 로고와 추가 CSS를, 메일은 공통 머리말·꼬리말을 쓴다. 파일은 <see cref="RequestPath"/> 아래에서 정적 파일로 나간다.
/// </summary>
public sealed class UiProfile
{
    /// <summary>프로필 파일을 내보내는 주소.</summary>
    public const string RequestPath = "/profile";

    /// <summary>로그인 화면 배치: 왼쪽 브랜드 + 오른쪽 입력(기본).</summary>
    public const string LoginSplit = "split";

    /// <summary>로그인 화면 배치: 가운데 카드 하나.</summary>
    public const string LoginCenter = "center";

    private readonly IReadOnlyDictionary<string, string> _versions;
    private readonly string? _mailHead;
    private readonly string? _mailTail;

    internal UiProfile(
        string name,
        string? rootPath,
        UiProfileFiles files,
        bool showSiteName,
        string loginLayout,
        string? reportCss,
        string? reportLogoData,
        string? mailLayout,
        IReadOnlyDictionary<string, string> versions)
    {
        Name = name;
        RootPath = rootPath;
        Files = files;
        ShowSiteName = showSiteName;
        LoginLayout = loginLayout;
        ReportCss = reportCss;
        ReportLogoData = reportLogoData;
        _versions = versions;
        if (mailLayout is not null)
        {
            var at = mailLayout.IndexOf(UiProfileLoader.MailBodyVariable, StringComparison.Ordinal);
            _mailHead = mailLayout[..at];
            _mailTail = mailLayout[(at + UiProfileLoader.MailBodyVariable.Length)..];
        }
    }

    /// <summary>프로필을 쓰지 않을 때(이름이 비어 있음): 프레임워크 기본 화면, 로고·파비콘 없음.</summary>
    public static UiProfile None { get; } = new("", null, new UiProfileFiles(null, null, null, null, false, false), true, LoginSplit, null, null, null,
        new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>프로필 이름. 프로필이 없으면 빈 문자열.</summary>
    public string Name { get; }

    /// <summary>프로필 폴더 전체 경로. 프로필이 없으면 null.</summary>
    public string? RootPath { get; }

    /// <summary>프로필에 들어 있는 파일 이름들.</summary>
    public UiProfileFiles Files { get; }

    /// <summary>로고 옆에 사이트명(<c>SITE.NAME</c>)을 글자로도 보일지. false면 로고 그림이 사이트명을 대신한다(alt).</summary>
    public bool ShowSiteName { get; }

    /// <summary>로그인 화면 배치 (<see cref="LoginSplit"/> 또는 <see cref="LoginCenter"/>).</summary>
    public string LoginLayout { get; }

    /// <summary>레포트·PDF에 덧붙일 CSS 내용(<c>report.css</c>). 없으면 null.</summary>
    public string? ReportCss { get; }

    /// <summary>레포트 머리말 로고(<c>data:</c> 형식, PDF는 외부 그림을 막으므로 문서에 넣는다). 없으면 null.</summary>
    public string? ReportLogoData { get; }

    /// <summary>메일 공통 머리말·꼬리말(<c>mail-layout.html</c>)이 있는지.</summary>
    public bool HasMailLayout => _mailHead is not null;

    /// <summary>프로필 파일의 주소(<c>/profile/logo.svg?v=해시</c>). 파일이 없으면 null. 내용이 바뀌면 해시가 바뀌어 브라우저 캐시가 갱신된다.</summary>
    public string? PathOf(string? file) =>
        file is not null && _versions.TryGetValue(file, out var version) ? $"{RequestPath}/{file}?v={version}" : null;

    /// <summary>
    /// 메일 본문을 공통 머리말·꼬리말로 감싼다. 머리말·꼬리말의 <c>{{SITE_NM}}</c> 등은 본문과 같은 값으로 치환(HTML 인코딩)하고,
    /// 이미 치환된 본문은 다시 치환하지 않는다. 레이아웃이 없으면 본문 그대로.
    /// </summary>
    public string WrapMailBody(string renderedBody, IReadOnlyDictionary<string, string?> values) =>
        _mailHead is null
            ? renderedBody
            : MessageRenderer.RenderBody(_mailHead, values) + renderedBody + MessageRenderer.RenderBody(_mailTail!, values);
}

/// <summary>프로필 폴더 안의 파일 이름 (폴더 기준, 하위 폴더 없음).</summary>
/// <param name="LogoLight">밝은 배경용 로고(상단 바). 없으면 사이트명 첫 글자를 보인다.</param>
/// <param name="LogoDark">어두운 화면(다크 모드)용 로고. 없으면 <paramref name="LogoLight"/>.</param>
/// <param name="LogoOnBrand">브랜드 색 배경(로그인 브랜드 영역)용 로고. 없으면 첫 글자.</param>
/// <param name="Favicon">파비콘(.ico/.png/.svg).</param>
/// <param name="HasTokens"><c>tokens.css</c>(색·글꼴·모서리 변수)가 있는지.</param>
/// <param name="HasReportCss"><c>report.css</c>가 있는지.</param>
public sealed record UiProfileFiles(string? LogoLight, string? LogoDark, string? LogoOnBrand, string? Favicon, bool HasTokens, bool HasReportCss);
