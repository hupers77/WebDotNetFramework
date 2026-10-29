using System.Globalization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Infrastructure.Files;
using Portal.Web.Models;

namespace Portal.Web.Controllers;

/// <summary>
/// 첨부 파일 공통 API (모든 화면의 첨부 부품이 쓴다). 권한은 첨부 묶음의 프로그램으로 확인한다:
/// 목록·다운로드(ZIP 포함)는 READ, 업로드·삭제는 CREATE 또는 UPDATE. 다른 회사 묶음은 SP가 없는 것처럼 응답한다(공유 묶음은 조회만 허용).
/// </summary>
[ProgramFromData]
public sealed class FileController(FileService files) : PortalController
{
    /// <summary>요청 본문 한도에 더하는 여유(멀티파트 머리글 등).</summary>
    private const long FormOverheadBytes = 1024 * 1024;

    /// <summary>
    /// 파일 하나 올리기 (멀티파트 <c>file</c>). 쿼리: <c>programId</c>, 기존 묶음에 추가하면 <c>fileGroupId</c>.
    /// 본문 한도를 설정(<c>FILE.MAX_SIZE_MB</c>)에 맞춘 뒤 읽어야 하므로 Action 파라미터 바인딩을 쓰지 않는다.
    /// </summary>
    [HttpPost]
    public async Task<ApiResult<FileUploadResult>> Upload()
    {
        var programId = Request.Query["programId"].ToString();
        long? fileGroupId = null;
        if (programId.Length is 0 or > 30)
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        if (Request.Query.TryGetValue("fileGroupId", out var groupText) && groupText.ToString().Length > 0)
        {
            fileGroupId = long.TryParse(groupText, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? id
                : throw new BizException(MessageKeys.InvalidInput);
        }

        await DemandAnyFuncAsync(programId, FuncCodes.Create, FuncCodes.Update);

        if (HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = files.MaxSizeBytes + FormOverheadBytes;
        }

        if (!Request.HasFormContentType)
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        IFormCollection form;
        try
        {
            form = await Request.ReadFormAsync(HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } or InvalidDataException)
        {
            throw new BizException(FileMessageKeys.TooLarge, files.MaxSizeMb);
        }

        if (form.Files is not [var file])
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        if (file.Length > files.MaxSizeBytes)
        {
            throw new BizException(FileMessageKeys.TooLarge, files.MaxSizeMb);
        }

        await using var content = file.OpenReadStream();
        return Ok(await files.UploadAsync(programId, fileGroupId, file.FileName, content, HttpContext.RequestAborted));
    }

    /// <summary>묶음의 파일 목록.</summary>
    [HttpGet]
    public async Task<ApiResult<FileListResult>> List(long fileGroupId)
    {
        var list = await files.GetListAsync(fileGroupId, HttpContext.RequestAborted) ?? throw new BizException(MessageKeys.NotFound);
        await DemandAnyFuncAsync(list.Group.ProgramId, FuncCodes.Read);
        return Ok(list);
    }

    /// <summary>다운로드. 항상 첨부로 내려 브라우저가 열어 실행하지 않게 한다.</summary>
    [HttpGet]
    public async Task<IActionResult> Download(long id)
    {
        var file = await files.GetAsync(id, HttpContext.RequestAborted) ?? throw new BizException(MessageKeys.NotFound);
        await DemandAnyFuncAsync(file.ProgramId, FuncCodes.Read);

        var content = await files.OpenDownloadAsync(file, HttpContext.RequestAborted);
        Response.Headers.CacheControl = "no-store";
        return File(content, "application/octet-stream", file.OrgFileNm);
    }

    /// <summary>
    /// 게시글 본문 사진 (편집기가 넣는 <c>/File/Image/{id}</c>). 사진 확장자(jpg·jpeg·png·gif)만 화면에 바로 보여 주고
    /// 그 밖의 파일은 없는 것처럼 응답한다. 다운로드 횟수는 올리지 않는다. 권한은 다운로드와 같다(묶음 프로그램 READ).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Image(long id)
    {
        var file = await files.GetAsync(id, HttpContext.RequestAborted) ?? throw new BizException(MessageKeys.NotFound);
        var contentType = FileService.ImageContentType(file.FileExt) ?? throw new BizException(MessageKeys.NotFound);
        await DemandAnyFuncAsync(file.ProgramId, FuncCodes.Read);

        Response.Headers.CacheControl = "private, max-age=86400";
        return File(files.OpenInline(file), contentType);
    }

    /// <summary>묶음의 파일을 ZIP 하나로 다운로드.</summary>
    [HttpGet]
    public async Task<IActionResult> DownloadZip(long id)
    {
        var list = await files.GetListAsync(id, HttpContext.RequestAborted) ?? throw new BizException(MessageKeys.NotFound);
        await DemandAnyFuncAsync(list.Group.ProgramId, FuncCodes.Read);
        if (list.Files.Count == 0)
        {
            throw new BizException(MessageKeys.NotFound);
        }

        var content = await files.OpenZipAsync(list, HttpContext.RequestAborted);
        Response.Headers.CacheControl = "no-store";
        return File(content, "application/zip", $"files-{id}.zip");
    }

    /// <summary>삭제 (삭제 표시만, 업무 저장 전 취소해도 되돌릴 수 있게).</summary>
    [HttpPost]
    public async Task<ApiResult> Delete([FromBody] FileDeleteRequest request)
    {
        var file = await files.GetAsync(request.FileId, HttpContext.RequestAborted) ?? throw new BizException(MessageKeys.NotFound);
        await DemandAnyFuncAsync(file.ProgramId, FuncCodes.Create, FuncCodes.Update);
        await files.DeleteAsync(file.FileId, HttpContext.RequestAborted);
        return Ok();
    }
}
