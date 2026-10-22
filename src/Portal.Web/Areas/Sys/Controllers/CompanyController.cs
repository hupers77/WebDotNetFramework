using Microsoft.AspNetCore.Mvc;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.Web.Areas.Sys.Controllers;

/// <summary>
/// 회사 관리 (표준 화면 3 마스터-디테일). 왼쪽 회사 목록에서 고르면 오른쪽에서 회사명·사업자등록번호·접속 허용 IP·계약 종료일·사용 여부를 고친다.
/// 신규는 협력사만 등록하고(회사 코드는 P0001부터 자동 부여), 잘못 등록해 아직 쓰이지 않은 협력사만 지운다.
/// </summary>
[Area("Sys")]
[Program(ProgramId)]
public sealed class CompanyController(ICompanyAdminService companies, ICodeAdminService codes) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "SYS_COMPANY_MGT";

    /// <summary>회사 유형 공통코드 그룹.</summary>
    private const string TypeGroup = "COMPANY_TYPE";

    /// <summary>화면.</summary>
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var types = (await codes.GetCodesAsync(TypeGroup, cancellationToken)).Codes.Items
            .Where(c => c.UseYn == "Y")
            .Select(c => (c.Code, c.CodeNm))
            .ToList();
        return View(new CompanyIndexModel(types));
    }

    /// <summary>회사 목록.</summary>
    [HttpGet]
    public async Task<ApiResult<PageResult<CompanyDto>>> List([FromQuery] CompanySearch search, CancellationToken cancellationToken) =>
        Ok(await companies.GetListAsync(search, cancellationToken));

    /// <summary>협력사 등록.</summary>
    [HttpPost, Func(FuncCodes.Create)]
    public async Task<ApiResult<CompanySaveResult>> Create([FromBody] CompanySaveRequest request, CancellationToken cancellationToken)
    {
        if (request.RowVer is not null || request.CompanyTypeCd != CompanyTypes.Partner)
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        return Ok(await companies.SaveAsync(request, cancellationToken));
    }

    /// <summary>회사 정보 수정.</summary>
    [HttpPost, Func(FuncCodes.Update)]
    public async Task<ApiResult<CompanySaveResult>> Save([FromBody] CompanySaveRequest request, CancellationToken cancellationToken)
    {
        if (request.RowVer is null || string.IsNullOrEmpty(request.CompanyCd))
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        return Ok(await companies.SaveAsync(request, cancellationToken));
    }

    /// <summary>협력사 삭제.</summary>
    [HttpPost, Func(FuncCodes.Delete)]
    public async Task<ApiResult> Delete([FromBody] CompanyDeleteRequest request, CancellationToken cancellationToken)
    {
        await companies.DeleteAsync(request, cancellationToken);
        return Ok();
    }
}
