using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Portal.Worker;

/// <summary>진입점. 웹의 <c>Program</c>과 이름이 겹치지 않게 따로 둔다(테스트 프로젝트가 둘 다 참조).</summary>
internal static class WorkerProgram
{
    /// <summary>
    /// Windows 서비스로 설치하면 서비스 관리자가 시작·중지한다(<c>sc.exe create PortalWorker binPath= ...</c>).
    /// 콘솔에서 실행하면 Ctrl+C로 멈춘다(개발 PC).
    /// </summary>
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(o => o.ServiceName = "PortalWorker");
        builder.Services.AddPortalWorker(builder.Configuration);
        await builder.Build().RunAsync();
    }
}
