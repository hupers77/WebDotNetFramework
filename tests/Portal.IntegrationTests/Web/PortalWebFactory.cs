using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Portal.Core.Config;
using Portal.Data;
using Portal.Framework.Web.Authentication;
using Portal.Framework.Web.Context;
using Portal.Framework.Web.Logging;
using Portal.Framework.Web.Navigation;
using Portal.Infrastructure.Localization;
using Portal.Security.Authentication;
using Portal.Security.Authorization;
using Portal.Testing;

namespace Portal.IntegrationTests.Web;

/// <summary>
/// 웹 통합 테스트 호스트 (개발 표준 가이드 9장). 실제 미들웨어·필터·Controller를 그대로 띄우고 DB만 <see cref="FakeSpExecutor"/>로 바꾼다.
/// 사용자는 <see cref="SignIn"/>으로 세션과 권한을 등록한 뒤 그 쿠키로 요청한다. 접근 기록 적재기는 빼고 대기열을 직접 확인한다.
/// </summary>
public sealed class PortalWebFactory : WebApplicationFactory<Program>
{
    private readonly ConcurrentDictionary<string, SessionUser> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (string[] Roles, string[] Funcs)> _permissions = new(StringComparer.Ordinal);

    public PortalWebFactory()
    {
        Sp.Returns(LangResources.ListSp, _ => Texts.Select(t => new LangResources.ResourceRow { LangCd = t.Lang, ResKey = t.Key, ResText = t.Text }).ToList());
        Sp.Returns("FW.USP_AUTH_SESSION_CHECK_EXEC", p => _sessions.GetValueOrDefault((string)p["P_SESSION_HASH"]!));
        Sp.Returns("FW.USP_AUTH_GET_PERMISSION", _ => PermissionsOfCurrentUser());
        Sp.Returns(LangResources.LanguageSp, new List<LangResources.LanguageRow>
        {
            new() { LangCd = "ko", LangNm = "한국어", DefaultYn = "Y" },
            new() { LangCd = "en", LangNm = "English" },
            new() { LangCd = "zh-CN", LangNm = "简体中文" },
        });
        Sp.Returns(MenuCatalog.ListSp, _ => new object[] { Menus.ToList(), MenuNames.ToList() });
    }

    /// <summary>
    /// 테스트 메뉴: 1 홈(DSH_HOME) / 2 테스트 폴더 → 3 테스트 화면(TST_SCREEN), 4 본사 전용 화면(SYS_X, EMP)
    /// / 5 협력사 폴더(PARTNER) → 6 협력사 화면(TST_SCREEN) / 7 외부 링크 / 8 잘못된 링크(javascript:)
    /// / 9 공통코드(SYS_CODE_MGT, 테스트 폴더 아래, EMP).
    /// </summary>
    internal static readonly MenuCatalog.MenuRow[] Menus =
    [
        new() { MenuId = 1, MenuTypeCd = "PAGE", ProgramId = "DSH_HOME", Url = "/", IconCd = "home", SortNo = 10 },
        new() { MenuId = 2, MenuTypeCd = "FOLDER", SortNo = 20 },
        new() { MenuId = 3, ParentMenuId = 2, MenuTypeCd = "PAGE", ProgramId = TestScreensController.ProgramId, Url = "/test-screen/page", SortNo = 10 },
        new() { MenuId = 4, ParentMenuId = 2, MenuTypeCd = "PAGE", ProgramId = "SYS_X", Url = "/sys/x", SortNo = 20, OpenScopeCd = "EMP" },
        new() { MenuId = 5, MenuTypeCd = "FOLDER", SortNo = 30, OpenScopeCd = "PARTNER" },
        new() { MenuId = 6, ParentMenuId = 5, MenuTypeCd = "PAGE", ProgramId = TestScreensController.ProgramId, Url = "/test-screen/page?partner=1", SortNo = 10 },
        new() { MenuId = 7, MenuTypeCd = "LINK", Url = "https://example.com/manual", SortNo = 40, NewWinYn = "Y" },
        new() { MenuId = 8, MenuTypeCd = "LINK", Url = "javascript:alert(1)", SortNo = 50 },
        new() { MenuId = 9, ParentMenuId = 2, MenuTypeCd = "PAGE", ProgramId = "SYS_CODE_MGT", Url = "/Sys/Code", SortNo = 30, OpenScopeCd = "EMP" },
    ];

    internal static readonly MenuCatalog.MenuNameRow[] MenuNames =
    [
        .. Menus.Select(m => new MenuCatalog.MenuNameRow { MenuId = m.MenuId, LangCd = "ko", MenuNm = $"메뉴{m.MenuId}" }),
        .. Menus.Select(m => new MenuCatalog.MenuNameRow { MenuId = m.MenuId, LangCd = "en", MenuNm = $"Menu{m.MenuId}" }),
    ];

    public FakeSpExecutor Sp { get; } = new();

    /// <summary>첨부 저장소 (메모리).</summary>
    public MemoryFileStorage Files { get; } = new();

    public FakePortalConfig Config { get; } = new FakePortalConfig().Set("AUTH.LOGIN_RATE_PER_MIN", "1000");

    /// <summary>화면에 쓰는 몇 개 문구 (나머지는 키가 그대로 보인다).</summary>
    public static readonly (string Lang, string Key, string Text)[] Texts =
    [
        ("ko", "MSG.FORBIDDEN", "권한이 없습니다."),
        ("en", "MSG.FORBIDDEN", "You do not have permission."),
        ("ko", "MSG.LOGIN_REQUIRED", "로그인이 필요합니다."),
        ("ko", "MSG.LOGIN_FAILED", "ID 또는 비밀번호가 올바르지 않습니다."),
        ("ko", "MSG.DUP_CODE", "이미 사용 중인 코드입니다."),
        ("ko", "MSG.SYSTEM_ERROR", "일시적인 오류가 발생했습니다."),
        ("ko", "VAL.MAX_LENGTH", "{0}은(는) {1}자 이하로 입력하세요."),
    ];

    /// <summary>세션을 만들고 쿠키에 넣을 토큰을 돌려준다. <paramref name="funcs"/>는 <c>"PROGRAM:FUNC"</c> 형식.</summary>
    public string SignIn(string userId, string[] funcs, string langCd = "ko", bool passwordChangeRequired = false, string[]? roles = null, string userTypeCd = "EMP")
    {
        var (token, hash) = SessionToken.Create();
        _sessions[hash] = new SessionUser
        {
            UserId = userId,
            CompanyCd = "HQ",
            UserTypeCd = userTypeCd,
            UserNm = userId,
            LangCd = langCd,
            PwdChgReqYn = passwordChangeRequired ? "Y" : "N",
            PwdChgDt = DateTime.UtcNow,
            ExpireDt = DateTime.UtcNow.AddMinutes(30),
        };
        _permissions[userId] = (roles ?? ["EMP_USER"], funcs);
        return token;
    }

    /// <summary>세션을 끝낸 것처럼 만든다 (다음 요청에 종료 사유가 돌아간다).</summary>
    public void EndSession(string token, string endReasonCd)
    {
        var key = SessionToken.Hash(token);
        _sessions[key] = new SessionUser { UserId = _sessions[key].UserId, EndReasonCd = endReasonCd };
    }

    /// <summary>
    /// https 주소로 쿠키·리디렉트를 직접 다루는 클라이언트 (<c>__Host-</c> 쿠키는 https에서만 보내진다).
    /// <paramref name="otherCookie"/>는 CSRF 쿠키처럼 함께 보낼 <c>이름=값</c>.
    /// </summary>
    public HttpClient NewClient(string? sessionToken = null, string? otherCookie = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = false });
        string?[] cookies = [sessionToken is null ? null : $"{SessionCookie.Name}={sessionToken}", otherCookie];
        if (cookies.Any(c => c is not null))
        {
            client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies.Where(c => c is not null)));
        }

        return client;
    }

    /// <summary>접근 기록 대기열에 쌓인 기록을 모두 꺼낸다.</summary>
    public List<AccessLogEntry> DrainAccessLog()
    {
        var buffer = Services.GetRequiredService<AccessLogBuffer>();
        var list = new List<AccessLogEntry>();
        while (buffer.Reader.TryRead(out var entry))
        {
            list.Add(entry);
        }

        return list;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Portal", "Server=not-used");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISpExecutor>();
            services.AddSingleton<ISpExecutor>(Sp);
            services.RemoveAll<IPortalConfig>();
            services.AddSingleton<IPortalConfig>(Config);
            services.RemoveAll<Portal.Infrastructure.Files.IFileStorage>();
            services.AddSingleton<Portal.Infrastructure.Files.IFileStorage>(Files);
            services.RemoveAll<IHostedService>();
            services.AddHostedService<Portal.Infrastructure.Caching.CacheVersionMonitor>();
            services.AddControllersWithViews().AddApplicationPart(typeof(PortalWebFactory).Assembly);
        });
    }

    private object[] PermissionsOfCurrentUser()
    {
        var http = Services.GetRequiredService<IHttpContextAccessor>().HttpContext;
        var userId = http?.Features.Get<PortalUserFeature>()?.User.UserId;
        if (userId is null || !_permissions.TryGetValue(userId, out var p))
        {
            return [Array.Empty<RoleScope>(), Array.Empty<ProgramFunc>(), Array.Empty<UserProgramFunc>()];
        }

        return
        [
            p.Roles.Select(r => new RoleScope { RoleId = r, DataScopeCd = DataScopes.Self }).ToList(),
            p.Funcs.Select(f => f.Split(':')).Select(f => new ProgramFunc { ProgramId = f[0], FuncCd = f[1] }).ToList(),
            Array.Empty<UserProgramFunc>(),
        ];
    }
}
