using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Portal.Core.Context;
using Portal.Framework.Web.Authorization;

namespace Portal.Framework.Web.Logging;

/// <summary>
/// 화면·API 접근 기록 (설계서 9장 '로그·추적', 10년 보관). 응답이 끝난 뒤 대기열에 넣기만 하므로 응답 시간에 영향이 없다.
/// 정적 파일·화면 문구 묶음·상태 확인은 기록하지 않는다. 쿼리 문자열은 남기지 않는다.
/// </summary>
public sealed class AccessLogMiddleware(RequestDelegate next, AccessLogBuffer queue, TimeProvider time)
{
    private const int MaxPathLength = 400;

    /// <summary>요청을 처리하고 기록을 넣는다.</summary>
    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser)
    {
        // 오류 화면 재실행(/Error/404 등)은 원래 요청이 이미 기록하므로 다시 남기지 않는다
        if (IsExcluded(context.Request.Path)
            || context.Features.Get<IStatusCodeReExecuteFeature>() is not null
            || context.Features.Get<IExceptionHandlerFeature>() is not null)
        {
            await next(context);
            return;
        }

        var started = time.GetTimestamp();
        var startedAt = time.GetUtcNow().UtcDateTime;
        short? failedStatus = null;
        try
        {
            await next(context);
        }
        catch
        {
            // 예외가 지나가는 시점에는 상태 코드가 아직 200이므로 500으로 남긴다
            failedStatus = StatusCodes.Status500InternalServerError;
            throw;
        }
        finally
        {
            var metadata = context.GetEndpoint()?.Metadata;
            var path = context.Request.PathBase + context.Request.Path;
            queue.Enqueue(new AccessLogEntry
            {
                LogDt = startedAt,
                UserId = currentUser.UserId,
                ProgramId = metadata?.GetMetadata<ProgramAttribute>()?.ProgramId,
                FuncCd = metadata?.GetMetadata<FuncAttribute>()?.FuncCd,
                HttpMethod = context.Request.Method,
                RequestPath = path.Value is { Length: > MaxPathLength } p ? p[..MaxPathLength] : path.Value ?? "/",
                StatusCode = failedStatus ?? (short)context.Response.StatusCode,
                ElapsedMs = (int)time.GetElapsedTime(started).TotalMilliseconds,
                ClientIp = currentUser.ClientIp,
                TraceId = Activity.Current?.Id ?? context.TraceIdentifier,
            });
        }
    }

    internal static bool IsExcluded(PathString path) =>
        path.StartsWithSegments("/css") || path.StartsWithSegments("/js") || path.StartsWithSegments("/lib")
        || path.StartsWithSegments("/img") || path.StartsWithSegments("/i18n") || path.StartsWithSegments("/health")
        || path.Equals("/favicon.ico") || path.Equals("/theme.css");
}
