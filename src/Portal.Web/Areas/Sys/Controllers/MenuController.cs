using Microsoft.AspNetCore.Mvc;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Infrastructure.Localization;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.Web.Areas.Sys.Controllers;

/// <summary>메뉴 관리 (표준 화면 2 그리드 편집형). 메뉴 세트의 트리를 부모·정렬 순서대로 보여 주고, 메뉴 추가·수정·삭제와 언어별 메뉴명을 한 번에 저장한다.</summary>
[Area("Sys")]
[Program(ProgramId)]
public sealed class MenuController(IMenuAdminService menus, LangResources langs) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "SYS_MENU_MGT";

    /// <summary>화면.</summary>
    public IActionResult Index() => View(new MenuIndexModel(langs.Languages));

    /// <summary>한 메뉴 세트의 메뉴 목록 (트리 순서).</summary>
    [HttpGet]
    public async Task<ApiResult<MenuListResult>> List([FromQuery] MenuSearch search, CancellationToken cancellationToken) =>
        Ok(await menus.GetListAsync(search.MenuSetCd, cancellationToken));

    /// <summary>바뀐 행 저장.</summary>
    [HttpPost, Func(FuncCodes.Update)]
    public async Task<ApiResult<MenuSaveResult>> Save([FromBody] MenuSaveRequest request, CancellationToken cancellationToken)
    {
        await DemandRowFuncsAsync(request.Rows);
        return Ok(await menus.SaveAsync(request, cancellationToken));
    }
}
