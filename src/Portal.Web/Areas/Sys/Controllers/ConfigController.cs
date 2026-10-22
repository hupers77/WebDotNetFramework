using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.Web.Areas.Sys.Controllers;

/// <summary>
/// 설정 관리 (표준 화면 2 그리드 편집형, 수정만). 분류·검색어로 찾고 값을 고쳐 한 번에 저장하며, 설정별 변경 이력을 본다.
/// 비밀값(SECRET)은 화면에 내보내지 않고, 새 값을 입력했을 때만 암호화해 바꾼다.
/// </summary>
[Area("Sys")]
[Program(ProgramId)]
public sealed class ConfigController(IConfigAdminService configs, ICodeAdminService codes) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "SYS_CONFIG_MGT";

    /// <summary>설정 분류 공통코드 그룹.</summary>
    private const string CategoryGroup = "CONFIG_CATEGORY";

    /// <summary>화면.</summary>
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var categories = (await codes.GetCodesAsync(CategoryGroup, cancellationToken)).Codes.Items
            .Where(c => c.UseYn == "Y")
            .Select(c => (c.Code, c.CodeNm))
            .ToList();
        return View(new ConfigIndexModel(categories));
    }

    /// <summary>설정 목록.</summary>
    [HttpGet]
    public async Task<ApiResult<PageResult<ConfigDto>>> List([FromQuery] ConfigSearch search, CancellationToken cancellationToken) =>
        Ok(await configs.GetListAsync(search, cancellationToken));

    /// <summary>설정 하나의 변경 이력.</summary>
    [HttpGet]
    public async Task<ApiResult<PageResult<ConfigHistDto>>> History(
        [Required(ErrorMessage = "MSG.INVALID_INPUT"), StringLength(100, ErrorMessage = "MSG.INVALID_INPUT")] string configKey,
        CancellationToken cancellationToken) =>
        Ok(await configs.GetHistoryAsync(configKey, cancellationToken));

    /// <summary>바뀐 설정 저장.</summary>
    [HttpPost, Func(FuncCodes.Update)]
    public async Task<ApiResult<ConfigSaveResult>> Save([FromBody] ConfigSaveRequest request, CancellationToken cancellationToken)
    {
        await DemandRowFuncsAsync(request.Rows);
        return Ok(await configs.SaveAsync(request, cancellationToken));
    }
}
