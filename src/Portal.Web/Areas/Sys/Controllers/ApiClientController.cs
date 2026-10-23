using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Api;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;
using Portal.Web.Areas.Sys.Models;
using Portal.Web.Areas.Sys.Services;

namespace Portal.Web.Areas.Sys.Controllers;

/// <summary>API 클라이언트 관리 (표준 화면 2 그리드 편집형). 비밀값은 발급 응답에서 한 번만 보여 준다.</summary>
[Area("Sys")]
[Program(ProgramId)]
[Route("Sys/ApiClient")]
public sealed class ApiClientController(IApiClientAdminService clients, EndpointDataSource endpoints) : PortalController
{
    /// <summary>프로그램 ID.</summary>
    public const string ProgramId = "SYS_API_CLIENT_MGT";

    /// <summary>화면.</summary>
    [HttpGet("")]
    public IActionResult Index() => View(new ApiClientIndexModel
    {
        Codes = [.. endpoints.Endpoints.OfType<RouteEndpoint>()
            .Select(e => (Operation: e.Metadata.GetMetadata<ApiOperation>(), e.RoutePattern.RawText))
            .Where(x => x.Operation is { Anonymous: false } && x.RawText is not null)
            .OrderBy(x => x.Operation!.Code, StringComparer.Ordinal)
            .Select(x => new ApiCodeInfo(x.Operation!.Code, x.Operation.Method, x.RawText!, x.Operation.Summary))],
    });

    /// <summary>클라이언트 목록.</summary>
    [HttpGet("List")]
    public async Task<ApiResult<PageResult<ApiClientDto>>> List(CancellationToken cancellationToken) =>
        Ok(await clients.GetListAsync(cancellationToken));

    /// <summary>바뀐 행 저장.</summary>
    [HttpPost("Save"), Func(FuncCodes.Update)]
    public async Task<ApiResult<ApiClientSaveResult>> Save([FromBody] ApiClientSaveRequest request, CancellationToken cancellationToken)
    {
        await DemandRowFuncsAsync(request.Rows);
        return Ok(await clients.SaveAsync(request, cancellationToken));
    }

    /// <summary>비밀값 (재)발급. 응답에만 원문이 있고 브라우저가 저장하지 않도록 한다.</summary>
    [HttpPost("Secret"), Func(FuncCodes.Update)]
    public async Task<ApiResult<ApiClientSecretResult>> Secret([FromBody] ApiClientSecretRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await clients.IssueSecretAsync(request, cancellationToken));
    }
}
