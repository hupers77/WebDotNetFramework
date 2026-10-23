using Microsoft.AspNetCore.Mvc;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.Web.Areas.Sys.Controllers;

/// <summary>휴일·달력 관리 (표준 화면 2 그리드 편집형). 연도를 고르면 그 해의 등록된 날짜를 보여 주고, 추가·수정·삭제를 한 번에 저장한다.</summary>
[Area("Sys")]
[Program(ProgramId)]
public sealed class HolidayController(IHolidayAdminService holidays, TimeProvider time) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "SYS_HOLIDAY_MGT";

    /// <summary>화면.</summary>
    public IActionResult Index() => View(new HolidaySearch { Year = time.GetLocalNow().Year });

    /// <summary>한 해의 휴일 목록.</summary>
    [HttpGet]
    public async Task<ApiResult<PageResult<HolidayDto>>> List([FromQuery] HolidaySearch search, CancellationToken cancellationToken) =>
        Ok(await holidays.GetListAsync(search.Year, cancellationToken));

    /// <summary>바뀐 행 저장.</summary>
    [HttpPost, Func(FuncCodes.Update)]
    public async Task<ApiResult<HolidaySaveResult>> Save([FromBody] HolidaySaveRequest request, CancellationToken cancellationToken)
    {
        await DemandRowFuncsAsync(request.Rows);
        return Ok(await holidays.SaveAsync(request, cancellationToken));
    }
}
