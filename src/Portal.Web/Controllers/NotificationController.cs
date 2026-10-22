using Microsoft.AspNetCore.Mvc;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Logging;
using Portal.Framework.Web.Mvc;
using Portal.Infrastructure.Messaging;
using Portal.Web.Models;

namespace Portal.Web.Controllers;

/// <summary>
/// 내 정보 > 알림함 (<c>MY_NOTIFICATION</c>)과 헤더 알림 배지. 모든 역할이 자기 알림만 본다(SP가 현재 사용자로 거른다).
/// 배지 조회(<see cref="Unread"/>)는 화면이 1분마다 부르므로 세션을 연장하지 않고(<c>X-Portal-Background</c>) 접속기록도 남기지 않는다.
/// </summary>
[Route("My/Notification")]
[Program(ProgramId)]
public sealed class NotificationController(NotificationService notifications, Portal.Core.Config.IPortalConfig config) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "MY_NOTIFICATION";

    /// <summary>화면.</summary>
    [HttpGet("")]
    public IActionResult Index() => View(new NotificationViewModel(config.GetInt(ConfigKeys.NotifyKeepDays)));

    /// <summary>내 알림 목록.</summary>
    [HttpGet("List")]
    public async Task<ApiResult<PageResult<NotificationItem>>> List([FromQuery] NotificationSearch search, CancellationToken cancellationToken) =>
        Ok(await notifications.GetListAsync(search.UnreadYn == "Y", search.PageNo, cancellationToken));

    /// <summary>안 읽은 알림 수 (헤더 배지).</summary>
    [HttpGet("Unread"), SkipAccessLog]
    public async Task<ApiResult<NotificationUnread>> Unread(CancellationToken cancellationToken) =>
        Ok(new NotificationUnread(await notifications.GetUnreadCountAsync(cancellationToken)));

    /// <summary>읽음 표시. <c>notiId</c>가 없으면 모두 읽음.</summary>
    [HttpPost("Read")]
    public async Task<ApiResult> Read([FromBody] NotificationReadRequest request, CancellationToken cancellationToken)
    {
        await notifications.MarkReadAsync(request.NotiId, cancellationToken);
        return Ok();
    }

    /// <summary>선택한 알림 삭제.</summary>
    [HttpPost("Delete"), Func(FuncCodes.Delete)]
    public async Task<ApiResult> Delete([FromBody] NotificationDeleteRequest request, CancellationToken cancellationToken)
    {
        if (request.NotiIds is not { Count: > 0 and <= NotificationService.MaxDelete })
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        await notifications.DeleteAsync(request.NotiIds, cancellationToken);
        return Ok();
    }
}
