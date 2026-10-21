using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// 서버 정보 노출 방지 (설계서 9장 '정보 노출')
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

var app = builder.Build();

var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

// 운영 모니터링용 상태 확인. DB·파일 저장소·AD 점검은 각 모듈이 추가될 때 붙인다.
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", version }));

await app.RunAsync();

/// <summary>웹 통합 테스트(WebApplicationFactory)에서 진입점을 참조하기 위한 선언.</summary>
public partial class Program;
