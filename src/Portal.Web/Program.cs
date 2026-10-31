using System.Reflection;
using Portal.Framework.Web;
using Portal.Security.Authentication;

var builder = WebApplication.CreateBuilder(args);

// 서버 정보 노출 방지 (설계서 9장 '정보 노출')
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
builder.Services.AddPortalWeb(builder.Configuration);
builder.Services.AddSingleton<Portal.Security.Mfa.IMfaCodeMailer, Portal.Web.Infrastructure.MfaCodeMailer>();

// 화면 Service (Area별)
builder.Services.AddScoped<Portal.Web.Areas.Sys.Services.ICodeAdminService, Portal.Web.Areas.Sys.Services.CodeAdminService>();
builder.Services.AddScoped<Portal.Web.Areas.Sys.Services.IConfigAdminService, Portal.Web.Areas.Sys.Services.ConfigAdminService>();
builder.Services.AddScoped<Portal.Web.Areas.Sys.Services.IPartnerApproveService, Portal.Web.Areas.Sys.Services.PartnerApproveService>();
builder.Services.AddScoped<Portal.Web.Areas.Sys.Services.IJobAdminService, Portal.Web.Areas.Sys.Services.JobAdminService>();
builder.Services.AddScoped<Portal.Web.Areas.Sys.Services.ICompanyAdminService, Portal.Web.Areas.Sys.Services.CompanyAdminService>();
builder.Services.AddScoped<Portal.Web.Areas.Sys.Services.IUserAdminService, Portal.Web.Areas.Sys.Services.UserAdminService>();
builder.Services.AddScoped<Portal.Web.Areas.Sys.Services.IRoleAuthService, Portal.Web.Areas.Sys.Services.RoleAuthService>();
builder.Services.AddScoped<Portal.Web.Areas.Sys.Services.IRoleAdminService, Portal.Web.Areas.Sys.Services.RoleAdminService>();
builder.Services.AddScoped<Portal.Web.Areas.Sys.Services.IProgramAdminService, Portal.Web.Areas.Sys.Services.ProgramAdminService>();
builder.Services.AddScoped<Portal.Web.Areas.Sys.Services.IUserAuthService, Portal.Web.Areas.Sys.Services.UserAuthService>();
builder.Services.AddScoped<Portal.Web.Areas.Log.Services.IAccessLogService, Portal.Web.Areas.Log.Services.AccessLogService>();
builder.Services.AddScoped<Portal.Web.Areas.Log.Services.IAuditLogService, Portal.Web.Areas.Log.Services.AuditLogService>();
builder.Services.AddScoped<Portal.Web.Areas.Log.Services.IPiiLogService, Portal.Web.Areas.Log.Services.PiiLogService>();
builder.Services.AddScoped<Portal.Web.Areas.Rpt.Services.IReportService, Portal.Web.Areas.Rpt.Services.ReportService>();
builder.Services.AddSingleton<Portal.Web.Areas.Rpt.Services.ReportRenderer>();
builder.Services.AddScoped<Portal.Web.Areas.Rpt.Services.IScreenPrintService, Portal.Web.Areas.Rpt.Services.ScreenPrintService>();
builder.Services.AddScoped<Portal.Web.Areas.Rpt.Services.IReportRunService, Portal.Web.Areas.Rpt.Services.ReportRunService>();
builder.Services.AddOptions<Portal.Web.Areas.Rpt.Services.ReportRunOptions>().Bind(builder.Configuration.GetSection(Portal.Web.Areas.Rpt.Services.ReportRunOptions.SectionName));
builder.Services.AddSingleton<Portal.Web.Areas.Rpt.Services.ReportRunProcessor>();
builder.Services.AddHostedService<Portal.Web.Areas.Rpt.Services.ReportRunWorker>();
builder.Services.AddScoped<Portal.Web.Areas.Sys.Services.IDeptAdminService, Portal.Web.Areas.Sys.Services.DeptAdminService>();
builder.Services.AddScoped<Portal.Web.Areas.Board.Services.IBoardService, Portal.Web.Areas.Board.Services.BoardService>();
builder.Services.AddScoped<Portal.Web.Areas.Board.Services.IBoardAdminService, Portal.Web.Areas.Board.Services.BoardAdminService>();
builder.Services.AddScoped<Portal.Web.Areas.Dash.Services.IDashService, Portal.Web.Areas.Dash.Services.DashService>();
builder.Services.AddScoped<Portal.Web.Areas.Dash.Services.IWidgetAdminService, Portal.Web.Areas.Dash.Services.WidgetAdminService>();
builder.Services.AddScoped<Portal.Web.Areas.Sys.Services.IHolidayAdminService, Portal.Web.Areas.Sys.Services.HolidayAdminService>();
builder.Services.AddScoped<Portal.Web.Areas.Sys.Services.IPopupAdminService, Portal.Web.Areas.Sys.Services.PopupAdminService>();
builder.Services.AddScoped<Portal.Web.Areas.Sys.Services.IApiClientAdminService, Portal.Web.Areas.Sys.Services.ApiClientAdminService>();
builder.Services.AddScoped<Portal.Web.Areas.Sys.Services.IApiOutboundAdminService, Portal.Web.Areas.Sys.Services.ApiOutboundAdminService>();

var app = builder.Build();

// 설치 직후 또는 비상시: dotnet Portal.Web.dll init-admin → 초기 관리자(sysadmin)를 만들거나 비밀번호를 재설정하고
// 임시 비밀번호를 한 번만 출력한다(초기 데이터 정의서 2장). 임시 비밀번호는 저장·기록하지 않는다.
if (args.Contains("init-admin", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var (created, tempPassword) = await scope.ServiceProvider.GetRequiredService<AdminBootstrapService>().InitAsync();
    Console.WriteLine(created ? "초기 관리자 sysadmin을 만들었습니다." : "sysadmin 비밀번호를 재설정했습니다.");
    Console.WriteLine($"임시 비밀번호: {tempPassword}  (첫 로그인 때 변경해야 하며 다시 보여 주지 않습니다)");
    return;
}

var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

app.UsePortalWeb();

// 운영 모니터링용 상태 확인. DB·파일 저장소·AD 점검은 각 모듈이 추가될 때 붙인다.
// 로그인 없이 열리는 주소이므로 버전(정보 노출)은 서버 자신(루프백)에서 부를 때만 준다.
app.MapGet("/health", (HttpContext http) =>
    http.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip)
        ? Results.Ok(new { status = "Healthy", version })
        : Results.Ok(new { status = "Healthy" }));

await app.RunAsync();

/// <summary>웹 통합 테스트(WebApplicationFactory)에서 진입점을 참조하기 위한 선언.</summary>
public partial class Program;
