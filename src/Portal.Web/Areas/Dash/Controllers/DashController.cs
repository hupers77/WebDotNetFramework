using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Dash.Models;
using Portal.Web.Areas.Dash.Services;

namespace Portal.Web.Areas.Dash.Controllers;

/// <summary>홈 대시보드의 위젯 목록과 데이터. 위젯마다 비동기로 불러, 느린 위젯이 있어도 홈 화면은 바로 보인다. 권한은 홈(<c>DSH_HOME</c>)과 위젯의 프로그램 둘 다 본다.</summary>
[Area("Dash")]
[Program("DSH_HOME")]
[Route("Dash")]
public sealed class DashController(IDashService dash, CurrentPermissions permissions) : PortalController
{
    /// <summary>볼 수 있는 위젯.</summary>
    [HttpGet("Widgets")]
    public async Task<ApiResult<IReadOnlyList<WidgetDto>>> Widgets() =>
        Ok(await dash.GetWidgetsAsync(await permissions.GetAsync(HttpContext.RequestAborted), HttpContext.RequestAborted));

    /// <summary>위젯 하나의 데이터.</summary>
    [HttpGet("Data")]
    public async Task<ApiResult<IReadOnlyList<WidgetRowDto>>> Data(
        [Required(ErrorMessage = "MSG.INVALID_INPUT"), RegularExpression("^[A-Z0-9_]{1,30}$", ErrorMessage = "MSG.INVALID_INPUT")] string widgetCd) =>
        Ok(await dash.GetDataAsync(widgetCd, await permissions.GetAsync(HttpContext.RequestAborted), HttpContext.RequestAborted));

    /// <summary>지금 띄울 공지 팝업.</summary>
    [HttpGet("Popups")]
    public async Task<ApiResult<IReadOnlyList<PopupActiveDto>>> Popups(CancellationToken cancellationToken) =>
        Ok(await dash.GetPopupsAsync(cancellationToken));
}
