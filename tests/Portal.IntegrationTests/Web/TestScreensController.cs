#pragma warning disable CA1822 // MVC Action은 인스턴스 메서드여야 발견된다

using Microsoft.AspNetCore.Mvc;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;

namespace Portal.IntegrationTests.Web;

/// <summary>공통 파이프라인 확인용 테스트 전용 화면 (프로그램 <c>TST_SCREEN</c>). 테스트 어셈블리에만 있다.</summary>
[Program(ProgramId)]
[Route("test-screen")]
public sealed class TestScreensController : PortalController
{
    public const string ProgramId = "TST_SCREEN";

    [HttpGet("list")]
    public ApiResult<string[]> List() => Ok<string[]>(["A", "B"]);

    [HttpPost("save"), Func(FuncCodes.Update)]
    public ApiResult Save([FromBody] SaveDto dto) => dto.Code == "DUP" ? throw new BizException(MessageKeys.DupCode) : Ok();

    [HttpGet("boom")]
    public IActionResult Boom() => throw new InvalidOperationException("민감한 내부 정보");

    [HttpGet("page")]
    public ContentResult Page() => Content("<p>ok</p>", "text/html");

    public sealed class SaveDto
    {
        public string Code { get; set; } = "";
    }
}

/// <summary>[Program]이 없는 Controller는 기본 거부되어야 한다.</summary>
[Route("test-unregistered")]
public sealed class TestUnregisteredController : PortalController
{
    [HttpGet("")]
    public ApiResult Index() => Ok();
}
