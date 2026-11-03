using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Portal.Data;
using Portal.Web.Areas.Sys.Models;

namespace Portal.Web.Areas.Sys.Services;

/// <summary>운영 모니터링 (<c>SYS_MONITOR_INQ</c>, 읽기 전용). DB 현황(세션·잠금·배치·대기열·오류·캐시)과 이 서버 프로세스 정보를 한 번에 돌려준다.</summary>
public interface IMonitorService
{
    /// <summary>현재 운영 현황.</summary>
    Task<MonitorResult> GetAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class MonitorService(ISpExecutor sp) : IMonitorService
{
    internal const string GetSp = "FW.USP_MONITOR_GET";

    /// <inheritdoc />
    public async Task<MonitorResult> GetAsync(CancellationToken cancellationToken = default)
    {
        var db = await sp.QueryMultipleAsync(
            GetSp,
            null,
            async reader => (
                Summary: await reader.ReadSingleAsync<MonitorSummary>(cancellationToken),
                Caches: await reader.ReadAsync<MonitorCache>(cancellationToken),
                Jobs: await reader.ReadAsync<MonitorJob>(cancellationToken),
                Mails: await reader.ReadAsync<MonitorStatus>(cancellationToken),
                Reports: await reader.ReadAsync<MonitorStatus>(cancellationToken),
                Errors: await reader.ReadAsync<MonitorError>(cancellationToken)),
            cancellationToken);
        return new MonitorResult
        {
            Summary = db.Summary ?? new MonitorSummary(),
            Caches = db.Caches,
            Jobs = db.Jobs,
            Mails = db.Mails,
            Reports = db.Reports,
            Errors = db.Errors,
            Server = ServerInfo(),
        };
    }

    /// <summary>이 프로세스 정보. 환경 변수·설정 값·경로는 담지 않는다.</summary>
    internal static MonitorServer ServerInfo()
    {
        using var process = Process.GetCurrentProcess();
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        return new MonitorServer(
            version.Split('+')[0],
            RuntimeInformation.FrameworkDescription,
            Environment.MachineName,
            process.StartTime.ToUniversalTime(),
            process.WorkingSet64 / (1024 * 1024),
            GC.GetTotalMemory(false) / (1024 * 1024),
            ThreadPool.ThreadCount,
            GC.CollectionCount(2));
    }
}
