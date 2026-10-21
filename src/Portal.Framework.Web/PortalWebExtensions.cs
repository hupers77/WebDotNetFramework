using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.WebEncoders;
using Portal.Core.Config;
using Portal.Core.Context;
using Portal.Core.Keys;
using Portal.Data;
using Portal.Framework.Web.Authentication;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Context;
using Portal.Framework.Web.Layout;
using Portal.Framework.Web.Logging;
using Portal.Framework.Web.Mvc;
using Portal.Framework.Web.Navigation;
using Portal.Framework.Web.Security;
using Portal.Infrastructure;
using Portal.Infrastructure.Caching;
using Portal.Security;

namespace Portal.Framework.Web;

/// <summary>웹 호스트 공통 구성 (설계서 4장 요청 처리 흐름). 호스트의 Program.cs는 이 두 메서드만 부른다.</summary>
public static class PortalWebExtensions
{
    /// <summary>로그인 요청 Rate Limit 정책 이름 (<c>AUTH.LOGIN_RATE_PER_MIN</c>, IP당 분당).</summary>
    public const string LoginRateLimitPolicy = "login";

    /// <summary>CSRF 토큰 요청 헤더 (<c>fw.ajax</c>가 자동으로 보낸다).</summary>
    public const string CsrfHeader = "X-CSRF-TOKEN";

    /// <summary>데이터·보안·설정·인증·권한·MVC·접근 기록 서비스를 등록한다.</summary>
    public static IServiceCollection AddPortalWeb(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddPortalData(configuration);
        services.AddPortalSecurity();
        services.AddPortalInfrastructure(configuration);

        // 한글·중국어를 &#x..; 로 바꾸지 않고 그대로 출력한다 (HTML 특수문자는 계속 인코딩된다)
        services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

        services.AddMemoryCache(o => o.SizeLimit = 100_000);
        services.AddSingleton<PermissionCache>();
        services.AddSingleton<ICacheVersionListener>(sp => sp.GetRequiredService<PermissionCache>());

        services.AddAuthentication(PortalSessionHandler.SchemeName)
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, PortalSessionHandler>(PortalSessionHandler.SchemeName, null);

        services.AddAntiforgery(o =>
        {
            o.HeaderName = CsrfHeader;
            o.Cookie.Name = "__Host-portal-csrf";
            o.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Strict;
        });

        services.AddScoped<CurrentPermissions>();
        services.AddScoped<PermissionFilter>();
        services.AddScoped<ShellResultFilter>();
        services.AddSingleton<MenuCatalog>();
        services.AddSingleton<ICacheVersionListener>(sp => sp.GetRequiredService<MenuCatalog>());
        services.AddScoped<MenuService>();
        services.AddScoped<UserLanguageService>();
        services.AddControllersWithViews(o =>
        {
            o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
            o.Filters.AddService<PermissionFilter>();
            o.Filters.Add<ApiExceptionFilter>();
            o.Filters.Add<ApiResultFilter>();
            o.Filters.AddService<ShellResultFilter>();
        });

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(LoginRateLimitPolicy, http =>
            {
                var limit = http.RequestServices.GetRequiredService<IPortalConfig>().GetInt(ConfigKeys.AuthLoginRatePerMin);
                return RateLimitPartition.GetFixedWindowLimiter(
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, limit), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
            });
            o.OnRejected = async (ctx, ct) =>
            {
                if (RequestKind.WantsJson(ctx.HttpContext.Request))
                {
                    await ctx.HttpContext.Response.WriteAsJsonAsync(ApiResults.Fail(ctx.HttpContext, MessageKeys.TooManyRequests), ct);
                }
            };
        });

        services.AddSingleton<AccessLogBuffer>();
        services.AddHostedService<AccessLogWriter>();
        return services;
    }

    /// <summary>
    /// 미들웨어 순서: 오류 화면 → HSTS(운영) → 보안 헤더 → 정적 파일 → 라우팅 → 접근 기록 → 인증 → Rate Limit → 권한(필터) → Controller.
    /// </summary>
    public static WebApplication UsePortalWeb(this WebApplication app)
    {
        app.UseExceptionHandler("/Error");
        app.UseStatusCodePagesWithReExecute("/Error/{0}");
        if (!app.Environment.IsDevelopmentLike())
        {
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseMiddleware<AccessLogMiddleware>();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        return app;
    }

    private static bool IsDevelopmentLike(this Microsoft.AspNetCore.Hosting.IWebHostEnvironment env) =>
        env.EnvironmentName is "Development" or "Testing";
}
