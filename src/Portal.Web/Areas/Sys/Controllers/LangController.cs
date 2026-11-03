using Microsoft.AspNetCore.Mvc;
using Portal.Core.Config;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Infrastructure.Files;
using Portal.Infrastructure.Localization;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.Web.Areas.Sys.Controllers;

/// <summary>
/// 다국어 문구 관리 (표준 화면 2 그리드 편집형). 키 하나에 사용 중인 모든 언어의 문구를 한 행으로 보여 주고,
/// 추가·수정·삭제와 엑셀 내보내기·업로드(미리보기)를 지원한다. 저장하면 문구 캐시가 바뀌어 웹서버가 5초 안에 새 문구를 쓴다.
/// </summary>
[Area("Sys")]
[Program(ProgramId)]
public sealed class LangController(ILangAdminService lang, LangResources langs, IPortalConfig config) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "SYS_LANG_MGT";

    /// <summary>화면.</summary>
    public IActionResult Index() => View(new LangIndexModel(langs.Languages));

    /// <summary>문구 키 목록 (한 쪽).</summary>
    [HttpGet]
    public async Task<ApiResult<PageResult<LangResourceDto>>> List([FromQuery] LangSearch search, CancellationToken cancellationToken) =>
        Ok(await lang.GetListAsync(search, cancellationToken));

    /// <summary>바뀐 행 저장.</summary>
    [HttpPost, Func(FuncCodes.Update)]
    public async Task<ApiResult<LangSaveResult>> Save([FromBody] LangSaveRequest request, CancellationToken cancellationToken)
    {
        await DemandRowFuncsAsync(request.Rows);
        return Ok(await lang.SaveAsync(request, cancellationToken));
    }

    /// <summary>검색 조건에 맞는 문구를 엑셀로 내려받는다.</summary>
    [HttpGet, Func(FuncCodes.Export)]
    public async Task<IActionResult> Export([FromQuery] LangSearch search, CancellationToken cancellationToken) =>
        Excel(await lang.ExportAsync(search, cancellationToken), "lang-resource.xlsx");

    /// <summary>엑셀 업로드 미리보기 (멀티파트 <c>file</c>). 저장하지 않고 그리드에 반영할 행과 행별 오류를 돌려준다.</summary>
    [HttpPost, Func(FuncCodes.Upload)]
    public async Task<ApiResult<LangUploadResult>> Upload(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        var maxMb = config.GetInt(ConfigKeys.FileMaxSizeMb);
        if (file.Length > maxMb * 1024L * 1024L)
        {
            throw new BizException(FileMessageKeys.TooLarge, maxMb);
        }

        await using var content = file.OpenReadStream();
        return Ok(await lang.PreviewUploadAsync(content, cancellationToken));
    }
}
