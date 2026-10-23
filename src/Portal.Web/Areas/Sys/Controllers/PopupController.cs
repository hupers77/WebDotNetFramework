using Microsoft.AspNetCore.Mvc;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Infrastructure.Localization;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;
using Portal.Core.Errors;

namespace Portal.Web.Areas.Sys.Controllers;

/// <summary>공지 팝업 관리 (표준 화면 3 마스터-디테일). 왼쪽 목록에서 고르면 오른쪽에서 기간·대상·언어별 문구를 고친다. 홈 화면에 뜨는 모양은 <c>Dash</c> 영역이 맡는다.</summary>
[Area("Sys")]
[Program(ProgramId)]
public sealed class PopupController(IPopupAdminService popups, LangResources langs) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "SYS_POPUP_MGT";

    /// <summary>화면.</summary>
    public IActionResult Index() => View(new PopupIndexModel(langs.Languages));

    /// <summary>팝업 목록.</summary>
    [HttpGet]
    public async Task<ApiResult<PageResult<PopupDto>>> List([FromQuery] PopupSearch search, CancellationToken cancellationToken) =>
        Ok(await popups.GetListAsync(search, cancellationToken));

    /// <summary>팝업 등록.</summary>
    [HttpPost, Func(FuncCodes.Create)]
    public async Task<ApiResult<PopupSaveResult>> Create([FromBody] PopupSaveRequest request, CancellationToken cancellationToken)
    {
        if (request.RowVer is not null || request.PopupId is not null)
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        return Ok(await popups.SaveAsync(request, cancellationToken));
    }

    /// <summary>팝업 수정.</summary>
    [HttpPost, Func(FuncCodes.Update)]
    public async Task<ApiResult<PopupSaveResult>> Save([FromBody] PopupSaveRequest request, CancellationToken cancellationToken)
    {
        if (request.RowVer is null || request.PopupId is null)
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        return Ok(await popups.SaveAsync(request, cancellationToken));
    }

    /// <summary>팝업 삭제.</summary>
    [HttpPost, Func(FuncCodes.Delete)]
    public async Task<ApiResult> Delete([FromBody] PopupDeleteRequest request, CancellationToken cancellationToken)
    {
        await popups.DeleteAsync(request, cancellationToken);
        return Ok();
    }
}
