using Microsoft.AspNetCore.Mvc;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Logging;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.Web.Areas.Sys.Controllers;

/// <summary>
/// 운영 모니터링 (읽기 전용 현황 화면). 접속 세션·잠긴 계정·배치 작업·메일 대기열·레포트 요청·캐시 버전·최근 오류와 이 서버의 메모리를 보여 준다.
/// 화면은 30초마다 현황을 다시 읽지만 사용자가 한 요청이 아니므로 세션을 연장하지 않고(<c>X-Portal-Background</c>) 접속기록도 남기지 않는다(화면을 연 기록은 남는다).
/// </summary>
[Area("Sys")]
[Program(ProgramId)]
public sealed class MonitorController(IMonitorService monitor) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "SYS_MONITOR_INQ";

    /// <summary>화면.</summary>
    public IActionResult Index() => View();

    /// <summary>현재 운영 현황.</summary>
    [HttpGet, SkipAccessLog]
    public async Task<ApiResult<MonitorResult>> Data(CancellationToken cancellationToken) =>
        Ok(await monitor.GetAsync(cancellationToken));
}
