using System.Globalization;
using System.Text.RegularExpressions;
using Portal.Framework.Web.Layout;
using Portal.Infrastructure.Ui;
using Portal.UnitTests.Architecture;

namespace Portal.UnitTests.Infrastructure;

/// <summary>디자인 프로필 읽기·검사, 메일 레이아웃, 배포하는 프로필(default, sample-hig, sample-dense, sample-office, sample-krds)의 형식과 명도 대비.</summary>
public sealed partial class UiProfileLoaderTests : IDisposable
{
    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"/>";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "portal-profiles-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static string ShippedRoot => Path.Combine(RepoPaths.Src, "Portal.Web", "Profiles");

    private UiProfile Load(string name) => UiProfileLoader.Load(new UiProfileOptions { Profile = name, ProfileRoot = _root }, Path.GetTempPath());

    private string Write(string profile, string file, string content)
    {
        var dir = Path.Combine(_root, profile);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, file), content);
        return dir;
    }

    [Fact]
    public void 이름이비면_프로필없음()
    {
        var profile = UiProfileLoader.Load(new UiProfileOptions { Profile = " " }, _root);

        Assert.Same(UiProfile.None, profile);
        Assert.Null(profile.RootPath);
        Assert.Null(profile.PathOf("favicon.png"));
        Assert.Equal("<p>본문</p>", profile.WrapMailBody("<p>본문</p>", new Dictionary<string, string?>()));
    }

    [Theory]
    [InlineData("../etc")]
    [InlineData("Default")]
    [InlineData("a/b")]
    [InlineData("-x")]
    public void 이름형식이틀리면_기동실패(string name)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Load(name));
        Assert.Contains("이름", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 폴더나_profile_json이없으면_기동실패()
    {
        Assert.Throws<InvalidOperationException>(() => Load("none"));

        Write("empty", "tokens.css", ":root{}");
        Assert.Throws<InvalidOperationException>(() => Load("empty"));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{ \"schemaVersion\": 2 }")]
    [InlineData("{ \"schemaVersion\": \"1\" }")]
    [InlineData("[]")]
    [InlineData("{ \"schemaVersion\": 1, \"login\": { \"layout\": \"top\" } }")]
    public void 설정형식이틀리면_기동실패(string json)
    {
        Write("bad", UiProfileLoader.ManifestFile, json);

        Assert.Throws<InvalidOperationException>(() => Load("bad"));
    }

    [Theory]
    [InlineData("../secret.svg")]
    [InlineData("sub/logo.svg")]
    [InlineData("logo.js")]
    [InlineData("logo.html")]
    [InlineData("missing.svg")]
    public void 로고파일은_폴더바로아래_허용확장자_있는파일만(string file)
    {
        Write("p", "logo.js", "alert(1)");
        Write("p", UiProfileLoader.ManifestFile, $$"""{ "schemaVersion": 1, "logo": { "light": "{{file}}" } }""");

        Assert.Throws<InvalidOperationException>(() => Load("p"));
    }

    [Fact]
    public void 너무큰파일은_거부()
    {
        Write("big", "logo.png", new string('x', (int)UiProfileLoader.MaxFileBytes + 1));
        Write("big", UiProfileLoader.ManifestFile, """{ "schemaVersion": 1, "logo": { "light": "logo.png" } }""");

        Assert.Throws<InvalidOperationException>(() => Load("big"));
    }

    [Fact]
    public void 정상프로필_파일주소는_해시버전_다크로고는_밝은로고로대체()
    {
        Write("brand", "logo.svg", Svg);
        Write("brand", "favicon.png", "png");
        Write("brand", UiProfileLoader.TokensFile, ":root { --fw-brand: #0066cc; }");
        Write("brand", UiProfileLoader.ReportCssFile, ".rpt-heading h1 { font-weight: 600; } </style>");
        Write("brand", UiProfileLoader.ManifestFile, """
            // 주석과 끝 쉼표를 허용한다
            { "schemaVersion": 1, "favicon": "favicon.png", "logo": { "light": "logo.svg", "showSiteName": false, }, "login": { "layout": "center" } }
            """);

        var profile = Load("brand");

        Assert.Equal("brand", profile.Name);
        Assert.Equal(Path.Combine(_root, "brand"), profile.RootPath);
        Assert.Equal("logo.svg", profile.Files.LogoDark);
        Assert.Null(profile.Files.LogoOnBrand);
        Assert.True(profile.Files.HasTokens);
        Assert.False(profile.ShowSiteName);
        Assert.Equal(UiProfile.LoginCenter, profile.LoginLayout);
        Assert.Matches("^/profile/logo\\.svg\\?v=[0-9A-F]{12}$", profile.PathOf("logo.svg"));
        Assert.NotNull(profile.PathOf(UiProfileLoader.TokensFile));
        Assert.Null(profile.PathOf(UiProfileLoader.ManifestFile));
        Assert.Null(profile.PathOf(null));
        Assert.StartsWith("data:image/svg+xml;base64,", profile.ReportLogoData, StringComparison.Ordinal);
        Assert.Contains("font-weight: 600", profile.ReportCss, StringComparison.Ordinal);
        Assert.False(profile.HasMailLayout);
    }

    [Fact]
    public void 파일내용이바뀌면_주소의버전도바뀐다()
    {
        Write("v", "logo.svg", Svg);
        Write("v", UiProfileLoader.ManifestFile, """{ "schemaVersion": 1, "logo": { "light": "logo.svg" }, "report": { "logo": false } }""");
        var before = Load("v");
        Write("v", "logo.svg", Svg.Replace("0 0 1 1", "0 0 2 2", StringComparison.Ordinal));

        Assert.NotEqual(before.PathOf("logo.svg"), Load("v").PathOf("logo.svg"));
        Assert.Null(before.ReportLogoData);
    }

    [Theory]
    [InlineData("<div>머리말</div>")]
    [InlineData("<div>{{BODY}}</div><p>{{BODY}}</p>")]
    public void 메일레이아웃에는_본문자리가_한번만(string layout)
    {
        Write("m", "mail.html", layout);
        Write("m", UiProfileLoader.ManifestFile, """{ "schemaVersion": 1, "mail": { "layout": "mail.html" } }""");

        Assert.Throws<InvalidOperationException>(() => Load("m"));
    }

    [Fact]
    public void 메일레이아웃_머리말꼬리말만치환하고_본문은그대로()
    {
        Write("m", "mail.html", "<h1>{{SITE_NM}}</h1>{{BODY}}<a href=\"{{LINK}}\">{{LINK}}</a>");
        Write("m", UiProfileLoader.ManifestFile, """{ "schemaVersion": 1, "mail": { "layout": "mail.html" } }""");
        var values = new Dictionary<string, string?>(StringComparer.Ordinal) { ["SITE_NM"] = "<포털>", ["LINK"] = "https://portal.example.com/" };

        var html = Load("m").WrapMailBody("<p>{{SITE_NM}} 그대로</p>", values);

        Assert.Equal("<h1>&lt;포털&gt;</h1><p>{{SITE_NM}} 그대로</p><a href=\"https://portal.example.com/\">https://portal.example.com/</a>", html);
    }

    [Fact]
    public void 정적파일은_화면에쓰는형식만_내보낸다()
    {
        Assert.Contains(".css", ProfileFiles.ContentTypes.Keys);
        Assert.Contains(".woff2", ProfileFiles.ContentTypes.Keys);
        Assert.DoesNotContain(".json", ProfileFiles.ContentTypes.Keys);
        Assert.DoesNotContain(".html", ProfileFiles.ContentTypes.Keys);
        Assert.DoesNotContain(".js", ProfileFiles.ContentTypes.Keys);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("sample-hig")]
    [InlineData("sample-dense")]
    [InlineData("sample-office")]
    [InlineData("sample-krds")]
    public void 배포하는프로필은_읽힌다(string name)
    {
        var profile = UiProfileLoader.Load(new UiProfileOptions { Profile = name, ProfileRoot = ShippedRoot }, _root);

        Assert.Equal(name, profile.Name);
        Assert.NotNull(profile.PathOf(profile.Files.Favicon));
    }

    [Fact]
    public void 웹설정의_기본프로필은_default()
    {
        var json = File.ReadAllText(Path.Combine(RepoPaths.Src, "Portal.Web", "appsettings.json"));

        Assert.Matches("\"Profile\":\\s*\"default\"", json);
    }

    /// <summary>프로필 tokens.css의 <c>@import url("../fonts/...")</c>는 <c>/profile/</c> 기준이라 wwwroot 아래 파일을 가리킨다. 그 파일과 글꼴 파일이 모두 있어야 한다.</summary>
    [Theory]
    [InlineData("sample-hig")]
    [InlineData("sample-krds")]
    public void 배포하는프로필의_웹글꼴은_wwwroot에있다(string name)
    {
        var wwwroot = Path.Combine(RepoPaths.Src, "Portal.Web", "wwwroot");
        var css = File.ReadAllText(Path.Combine(ShippedRoot, name, UiProfileLoader.TokensFile));
        var import = FontImport().Match(css);
        Assert.True(import.Success, $"{name}: 글꼴 @import가 없습니다");
        Assert.Contains("\"Noto Sans KR\"", css, StringComparison.Ordinal);

        var fontCss = Path.Combine(wwwroot, import.Groups["path"].Value.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(fontCss), fontCss);
        var dir = Path.GetDirectoryName(fontCss)!;
        var files = FontFile().Matches(File.ReadAllText(fontCss)).Select(m => m.Groups["file"].Value).ToList();
        Assert.NotEmpty(files);
        Assert.All(files, f => Assert.True(File.Exists(Path.Combine(dir, f)), f));
        Assert.True(File.Exists(Path.Combine(dir, "OFL.txt")), "글꼴 라이선스 파일(OFL.txt)이 없습니다");
    }

    /// <summary>개발 표준 가이드 5장: 글자와 배경의 명도 대비 4.5:1 이상. 프로필 tokens.css가 portal.css 기본값을 덮어쓴 결과로 검사한다.</summary>
    [Theory]
    [InlineData("default", false)]
    [InlineData("default", true)]
    [InlineData("sample-hig", false)]
    [InlineData("sample-hig", true)]
    [InlineData("sample-dense", false)]
    [InlineData("sample-dense", true)]
    [InlineData("sample-office", false)]
    [InlineData("sample-office", true)]
    [InlineData("sample-krds", false)]
    [InlineData("sample-krds", true)]
    public void 배포하는프로필의_글자색은_명도대비4_5이상(string name, bool dark)
    {
        var vars = Tokens(File.ReadAllText(Path.Combine(RepoPaths.Src, "Portal.Web", "wwwroot", "css", "portal.css")), dark);
        var tokensPath = Path.Combine(ShippedRoot, name, UiProfileLoader.TokensFile);
        if (File.Exists(tokensPath))
        {
            foreach (var (key, value) in Tokens(File.ReadAllText(tokensPath), dark))
            {
                vars[key] = value;
            }
        }

        vars["primary"] = dark ? vars["brand-dark"] : vars["brand"];
        (string Fore, string Back)[] pairs =
        [
            ("text", "bg"), ("text", "surface"), ("muted", "surface"), ("subtle", "surface"), ("error", "surface"),
            ("on-primary", "primary"), ("on-badge", "badge"), ("on-badge", "badge-new"), ("toast-text", "toast-bg"),
        ];
        foreach (var (fore, back) in pairs)
        {
            var ratio = Contrast(vars[fore], vars[back]);
            Assert.True(ratio >= 4.5, $"{name}{(dark ? " 다크" : "")}: --fw-{fore} {vars[fore]} / --fw-{back} {vars[back]} = {ratio:0.00}");
        }
    }

    /// <summary>밝은 값(다크 블록 앞) 위에 다크 값(<c>prefers-color-scheme: dark</c> 뒤)을 덮는다. #rrggbb 값만 읽는다.</summary>
    private static Dictionary<string, string> Tokens(string css, bool dark)
    {
        var at = css.IndexOf("@media (prefers-color-scheme: dark)", StringComparison.Ordinal);
        var vars = new Dictionary<string, string>(StringComparer.Ordinal);
        var parts = new List<string> { at >= 0 ? css[..at] : css };
        if (dark && at >= 0)
        {
            parts.Add(css[at..]);
        }

        foreach (var part in parts)
        {
            foreach (Match m in HexVar().Matches(part))
            {
                vars[m.Groups[1].Value] = m.Groups[2].Value;
            }
        }

        return vars;
    }

    private static double Contrast(string a, string b)
    {
        double l1 = Luminance(a), l2 = Luminance(b);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    private static double Luminance(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 3)
        {
            hex = string.Concat(hex.Select(c => new string(c, 2)));
        }

        double Channel(int i)
        {
            var c = int.Parse(hex.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(0)) + (0.7152 * Channel(2)) + (0.0722 * Channel(4));
    }

    [GeneratedRegex(@"--fw-([a-z0-9-]+):\s*(#[0-9a-fA-F]{3}(?:[0-9a-fA-F]{3})?)\s*;")]
    private static partial Regex HexVar();

    [GeneratedRegex("@import url\\(\"\\.\\./(?<path>[^\"]+)\"\\)")]
    private static partial Regex FontImport();

    [GeneratedRegex(@"url\(\./(?<file>[^)]+\.woff2)\)")]
    private static partial Regex FontFile();
}
