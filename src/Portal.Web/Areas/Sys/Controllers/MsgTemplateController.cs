using Microsoft.AspNetCore.Mvc;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Infrastructure.Localization;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.Web.Areas.Sys.Controllers;

/// <summary>
/// 메시지 템플릿 관리 (표준 화면 3 마스터-디테일). 왼쪽 목록에서 템플릿을 고르면 오른쪽에서 발송 방식과 언어별 제목·본문을 고친다.
/// 템플릿 ID와 연결 화면은 코드·초기 데이터가 정하므로 등록·삭제는 없다.
/// </summary>
[Area("Sys")]
[Program(ProgramId)]
public sealed class MsgTemplateController(IMsgTemplateAdminService templates, LangResources langs) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "SYS_MSG_TEMPLATE_MGT";

    /// <summary>화면.</summary>
    public IActionResult Index() => View(new MsgTemplateIndexModel(langs.Languages));

    /// <summary>템플릿 목록 (언어별 제목·본문 포함).</summary>
    [HttpGet]
    public async Task<ApiResult<IReadOnlyList<MsgTemplateDto>>> List([FromQuery] MsgTemplateSearch search, CancellationToken cancellationToken) =>
        Ok(await templates.GetListAsync(search, cancellationToken));

    /// <summary>템플릿 수정.</summary>
    [HttpPost, Func(FuncCodes.Update)]
    public async Task<ApiResult<MsgTemplateSaveResult>> Save([FromBody] MsgTemplateSaveRequest request, CancellationToken cancellationToken) =>
        Ok(await templates.SaveAsync(request, cancellationToken));
}
