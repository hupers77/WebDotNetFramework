using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Portal.Infrastructure.Ui;

/// <summary>
/// 디자인 프로필 폴더를 읽고 검사한다. 이름이 틀리거나 폴더·<c>profile.json</c>이 없거나 형식이 맞지 않으면 예외를 던져 기동을 멈춘다
/// (설치 실수를 운영 중이 아니라 배포할 때 알게 한다). 파일 이름은 프로필 폴더 바로 아래의 정해진 확장자만 받는다.
/// </summary>
public static partial class UiProfileLoader
{
    /// <summary><see cref="UiProfileOptions.ProfileRoot"/>를 비웠을 때 쓰는 폴더(실행 폴더 기준).</summary>
    public const string DefaultFolder = "Profiles";

    /// <summary>프로필 설정 파일.</summary>
    public const string ManifestFile = "profile.json";

    /// <summary>색·글꼴·모서리 변수 파일(있으면 <c>portal.css</c> 다음에 연결).</summary>
    public const string TokensFile = "tokens.css";

    /// <summary>레포트·PDF 추가 CSS(있으면 <c>report.css</c> 다음에 넣는다).</summary>
    public const string ReportCssFile = "report.css";

    /// <summary>지원하는 <c>profile.json</c> 형식 번호.</summary>
    public const int SchemaVersion = 1;

    /// <summary>메일 레이아웃에서 본문이 들어갈 자리.</summary>
    public const string MailBodyVariable = "{{BODY}}";

    /// <summary>프로필 파일 하나의 최대 크기.</summary>
    public const long MaxFileBytes = 1024 * 1024;

    private static readonly string[] LogoExtensions = [".svg", ".png", ".webp"];
    private static readonly string[] FaviconExtensions = [".ico", ".png", ".svg"];
    private static readonly string[] MailExtensions = [".html"];

    /// <summary>설정에 따라 프로필을 읽는다. 이름이 비어 있으면 <see cref="UiProfile.None"/>.</summary>
    /// <param name="options">설정.</param>
    /// <param name="baseDirectory"><see cref="UiProfileOptions.ProfileRoot"/>가 비었을 때 기준 폴더(보통 실행 폴더).</param>
    public static UiProfile Load(UiProfileOptions options, string baseDirectory)
    {
        var name = options.Profile.Trim();
        if (name.Length == 0)
        {
            return UiProfile.None;
        }

        if (!ProfileName().IsMatch(name))
        {
            throw Invalid(name, "이름은 영문 소문자·숫자·'-'로 40자 이내여야 합니다");
        }

        var root = string.IsNullOrWhiteSpace(options.ProfileRoot) ? Path.Combine(baseDirectory, DefaultFolder) : options.ProfileRoot.Trim();
        var dir = Path.GetFullPath(Path.Combine(root, name));
        if (!Directory.Exists(dir))
        {
            throw Invalid(name, $"폴더가 없습니다 ({dir})");
        }

        var manifestPath = Path.Combine(dir, ManifestFile);
        if (!File.Exists(manifestPath))
        {
            throw Invalid(name, $"{ManifestFile}이 없습니다");
        }

        using var manifest = ParseManifest(name, manifestPath);
        var json = manifest.RootElement;
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty("schemaVersion", out var schema)
            || schema.ValueKind != JsonValueKind.Number
            || !schema.TryGetInt32(out var schemaVersion)
            || schemaVersion != SchemaVersion)
        {
            throw Invalid(name, "schemaVersion은 1이어야 합니다");
        }

        var logoLight = FileRef(name, dir, Text(json, "logo", "light"), LogoExtensions);
        var logoDark = FileRef(name, dir, Text(json, "logo", "dark"), LogoExtensions);
        var logoOnBrand = FileRef(name, dir, Text(json, "logo", "onBrand"), LogoExtensions);
        var favicon = FileRef(name, dir, Text(json, "favicon", null), FaviconExtensions);
        var mailFile = FileRef(name, dir, Text(json, "mail", "layout"), MailExtensions);
        var hasTokens = OptionalFile(name, dir, TokensFile);
        var hasReportCss = OptionalFile(name, dir, ReportCssFile);

        var loginLayout = Text(json, "login", "layout") ?? UiProfile.LoginSplit;
        if (loginLayout is not (UiProfile.LoginSplit or UiProfile.LoginCenter))
        {
            throw Invalid(name, $"login.layout은 {UiProfile.LoginSplit} 또는 {UiProfile.LoginCenter}입니다");
        }

        string? mailLayout = null;
        if (mailFile is not null)
        {
            mailLayout = File.ReadAllText(Path.Combine(dir, mailFile));
            var first = mailLayout.IndexOf(MailBodyVariable, StringComparison.Ordinal);
            if (first < 0 || first != mailLayout.LastIndexOf(MailBodyVariable, StringComparison.Ordinal))
            {
                throw Invalid(name, $"{mailFile}에는 {MailBodyVariable}가 한 번만 있어야 합니다");
            }
        }

        string? reportLogo = null;
        if (logoLight is not null && (Flag(json, "report", "logo") ?? true))
        {
            reportLogo = $"data:{MediaType(logoLight)};base64,{Convert.ToBase64String(File.ReadAllBytes(Path.Combine(dir, logoLight)))}";
        }

        var versions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in new[] { logoLight, logoDark, logoOnBrand, favicon, hasTokens ? TokensFile : null, hasReportCss ? ReportCssFile : null })
        {
            if (file is not null)
            {
                versions[file] = Version(Path.Combine(dir, file));
            }
        }

        return new UiProfile(
            name,
            dir,
            new UiProfileFiles(logoLight, logoDark ?? logoLight, logoOnBrand, favicon, hasTokens, hasReportCss),
            Flag(json, "logo", "showSiteName") ?? true,
            loginLayout,
            hasReportCss ? File.ReadAllText(Path.Combine(dir, ReportCssFile)) : null,
            reportLogo,
            mailLayout,
            versions);
    }

    private static JsonDocument ParseManifest(string name, string path)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"디자인 프로필 '{name}': {ManifestFile} 형식이 잘못되었습니다 ({ex.Message})", ex);
        }
    }

    /// <summary><c>section.property</c>(property가 null이면 <c>section</c>) 문자열 값. 없거나 빈 값이면 null.</summary>
    private static string? Text(JsonElement json, string section, string? property)
    {
        if (!json.TryGetProperty(section, out var value))
        {
            return null;
        }

        if (property is not null)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(property, out value))
            {
                return null;
            }
        }

        return value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;
    }

    private static bool? Flag(JsonElement json, string section, string property) =>
        json.TryGetProperty(section, out var value) && value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;

    /// <summary>profile.json이 가리키는 파일: 폴더 바로 아래, 허용 확장자, 크기 제한.</summary>
    private static string? FileRef(string name, string dir, string? file, string[] extensions)
    {
        if (file is null)
        {
            return null;
        }

        if (!FileName().IsMatch(file) || file.Contains("..", StringComparison.Ordinal)
            || !extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
        {
            throw Invalid(name, $"파일 이름 '{file}'은 쓸 수 없습니다 (허용 확장자: {string.Join(", ", extensions)})");
        }

        if (!OptionalFile(name, dir, file))
        {
            throw Invalid(name, $"파일 '{file}'이 없습니다");
        }

        return file;
    }

    private static bool OptionalFile(string name, string dir, string file)
    {
        var info = new FileInfo(Path.Combine(dir, file));
        if (!info.Exists)
        {
            return false;
        }

        if (info.Length > MaxFileBytes)
        {
            throw Invalid(name, $"파일 '{file}'이 1MB를 넘습니다");
        }

        return true;
    }

    private static string MediaType(string file) => Path.GetExtension(file).ToUpperInvariant() switch
    {
        ".SVG" => "image/svg+xml",
        ".PNG" => "image/png",
        _ => "image/webp",
    };

    private static string Version(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)), 0, 6);

    private static InvalidOperationException Invalid(string name, string reason) => new($"디자인 프로필 '{name}': {reason}");

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProfileName();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex FileName();
}
