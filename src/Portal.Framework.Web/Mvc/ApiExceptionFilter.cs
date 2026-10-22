using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Core.Results;
using Portal.Framework.Web.Authorization;

namespace Portal.Framework.Web.Mvc;

/// <summary>
/// 오류 처리 흐름 (개발 표준 가이드 7장): 업무 오류는 200 + <c>success:false</c>, Action 안의 권한 확인 실패는 403, 예상 못한 오류는 기록 후 500 + 추적 ID.
/// 화면(HTML) 요청의 예상 못한 오류는 공통 오류 화면(<c>UseExceptionHandler</c>)으로 넘긴다.
/// </summary>
public sealed partial class ApiExceptionFilter(ILogger<ApiExceptionFilter> logger) : IExceptionFilter
{
    /// <inheritdoc />
    public void OnException(ExceptionContext context)
    {
        var http = context.HttpContext;
        if (context.Exception is BizException biz)
        {
            context.Result = new ObjectResult(ApiResults.Fail(http, biz.MessageKey, [.. biz.Args])) { StatusCode = StatusCodes.Status200OK };
            context.ExceptionHandled = true;
            return;
        }

        if (context.Exception is PermissionDeniedException)
        {
            context.Result = RequestKind.WantsJson(http.Request)
                ? new ObjectResult(ApiResults.Fail(http, MessageKeys.Forbidden)) { StatusCode = StatusCodes.Status403Forbidden }
                : new ForbidResult();
            context.ExceptionHandled = true;
            return;
        }

        if (!RequestKind.WantsJson(http.Request) || context.Exception is OperationCanceledException)
        {
            return;
        }

        LogUnhandled(logger, ApiResults.TraceId(http), context.Exception);
        context.Result = new ObjectResult(ApiResults.Fail(http, MessageKeys.SystemError)) { StatusCode = StatusCodes.Status500InternalServerError };
        context.ExceptionHandled = true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "처리하지 못한 오류 (추적 ID {TraceId})")]
    private static partial void LogUnhandled(ILogger logger, string traceId, Exception ex);
}

/// <summary>
/// Action이 돌려준 <see cref="ApiResult"/>에 추적 ID를 채우고, 모델 검증 실패를 400 + 첫 번째 검증 메시지로 바꾼다.
/// 검증 특성의 <c>ErrorMessage</c>에는 <c>VAL.</c> 키를 쓴다.
/// </summary>
public sealed class ApiResultFilter : IAsyncActionFilter, IResultFilter
{
    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.ModelState.IsValid && RequestKind.WantsJson(context.HttpContext.Request))
        {
            var key = context.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault(m => m.Length > 0)
                ?? MessageKeys.InvalidInput;
            context.Result = new ObjectResult(ApiResults.Fail(context.HttpContext, key)) { StatusCode = StatusCodes.Status400BadRequest };
            return;
        }

        await next();
    }

    /// <inheritdoc />
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is ObjectResult { Value: ApiResult result } && result.TraceId is null)
        {
            result.TraceId = ApiResults.TraceId(context.HttpContext);
        }
    }

    /// <inheritdoc />
    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
