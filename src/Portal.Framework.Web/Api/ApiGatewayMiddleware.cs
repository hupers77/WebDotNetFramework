using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Portal.Core.Config;
using Portal.Core.Keys;

namespace Portal.Framework.Web.Api;

/// <summary>
/// 외부 API(<c>/api/v1</c>) 관문. 토큰 확인 → 클라이언트 사용 여부·허용 IP → API 코드 허용 → 분당 호출 한도 순으로 걸러 낸 뒤 Handler를 실행하고,
/// 모든 요청·응답을 민감 값을 가려 <c>LOG.TB_API_LOG</c>에 남긴다. 오류는 개발 가이드 8장의 JSON 형식(<c>{"success":false,"error":{"code","message"}}</c>)으로 답한다(토큰 발급만 OAuth2 형식).
/// </summary>
internal sealed partial class ApiGatewayMiddleware(
    RequestDelegate next,
    ApiLogBuffer logs,
    ApiRateLimiter limiter,
    TimeProvider time,
    ILogger<ApiGatewayMiddleware> logger)
{
    private const int CaptureBytes = 16 * 1024;

    /// <summary>요청을 처리한다.</summary>
    public async Task InvokeAsync(HttpContext context, ApiJwt jwt, IApiClientStore clients, IPortalConfig config)
    {
        var started = time.GetTimestamp();
        var startedAt = time.GetUtcNow().UtcDateTime;
        var operation = context.GetEndpoint()?.Metadata.GetMetadata<ApiOperation>();
        var isToken = operation?.Code == ApiEndpointExtensions.TokenCode;

        string? requestBody = null;
        if (operation is not null && !isToken && HasBody(context.Request))
        {
            requestBody = await ReadRequestPreviewAsync(context.Request);
        }

        var original = context.Response.Body;
        var capture = new CaptureStream(original, CaptureBytes);
        context.Response.Body = capture;
        string? clientId = null;
        short? failedStatus = null;
        try
        {
            if (operation is null)
            {
                await WriteErrorAsync(context, StatusCodes.Status404NotFound, "not_found", "Unknown API.");
                return;
            }

            if (!operation.Anonymous)
            {
                var caller = jwt.Validate(BearerToken(context.Request));
                if (caller is null)
                {
                    context.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
                    await WriteErrorAsync(context, StatusCodes.Status401Unauthorized, "invalid_token", "Missing, malformed or expired token.");
                    return;
                }

                clientId = caller.ClientId;
                var client = await clients.GetAsync(caller.ClientId, context.RequestAborted);
                if (client is null || !client.Active || client.IpListInvalid || !client.AllowIps.Allows(context.Connection.RemoteIpAddress))
                {
                    await WriteErrorAsync(context, StatusCodes.Status403Forbidden, "access_denied", "The client is disabled or not allowed from this address.");
                    return;
                }

                // 토큰이 아니라 현재 설정의 허용 목록을 기준으로 한다 (허용을 회수하면 토큰이 남아 있어도 막힌다)
                if (!client.ApiCodes.Contains(operation.Code) || !caller.ApiCodes.Contains(operation.Code))
                {
                    await WriteErrorAsync(context, StatusCodes.Status403Forbidden, "insufficient_scope", "This API is not allowed for the client.");
                    return;
                }

                if (!limiter.TryAcquire("client:" + client.ClientId, client.RatePerMin ?? config.GetInt(ConfigKeys.ApiDefaultRatePerMin), out var retryAfter))
                {
                    context.Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    await WriteErrorAsync(context, StatusCodes.Status429TooManyRequests, "too_many_requests", "Rate limit exceeded.");
                    return;
                }

                context.Items[ApiCallerExtensions.ItemKey] = new ApiCaller(client.ClientId, client.ApiCodes);
            }
            else if (isToken && !limiter.TryAcquire("token:" + context.Connection.RemoteIpAddress, config.GetInt(ConfigKeys.AuthLoginRatePerMin), out var wait))
            {
                context.Response.Headers.RetryAfter = wait.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await WriteErrorAsync(context, StatusCodes.Status429TooManyRequests, "too_many_requests", "Rate limit exceeded.");
                return;
            }

            await next(context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, context.Request.Path, ex);
            failedStatus = StatusCodes.Status500InternalServerError;
            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                await WriteErrorAsync(context, StatusCodes.Status500InternalServerError, "server_error", "Internal error.");
            }
        }
        finally
        {
            context.Response.Body = original;
            // 토큰 발급 요청은 비밀값이 오가므로 본문을 아예 남기지 않는다
            var respBody = isToken ? null : ApiLogMasker.MaskBody(capture.Preview(), context.Response.ContentType);
            logs.Enqueue(new ApiLogEntry
            {
                LogDt = startedAt,
                DirectionCd = "IN",
                ClientId = clientId,
                HttpMethod = context.Request.Method.Length > 10 ? context.Request.Method[..10] : context.Request.Method,
                Url = ApiLogMasker.MaskUrl(context.Request.PathBase + context.Request.Path, context.Request.QueryString.Value),
                StatusCode = failedStatus ?? (short)context.Response.StatusCode,
                ElapsedMs = (int)time.GetElapsedTime(started).TotalMilliseconds,
                RequestBody = isToken ? null : ApiLogMasker.MaskBody(requestBody, context.Request.ContentType),
                ResponseBody = respBody,
                TraceId = Activity.Current?.Id ?? context.TraceIdentifier,
            });
        }
    }

    internal static async Task WriteErrorAsync(HttpContext context, int status, string error, string description)
    {
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(new
        {
            success = false,
            error = new { code = error, message = description },
            traceId = Activity.Current?.Id ?? context.TraceIdentifier,
        });
    }

    private static string? BearerToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : null;
    }

    private static bool HasBody(HttpRequest request) =>
        request.ContentLength is > 0 || request.Headers.ContainsKey("Transfer-Encoding");

    private static async Task<string?> ReadRequestPreviewAsync(HttpRequest request)
    {
        request.EnableBuffering(CaptureBytes * 4);
        var buffer = new byte[CaptureBytes];
        var read = 0;
        try
        {
            int n;
            while (read < buffer.Length && (n = await request.Body.ReadAsync(buffer.AsMemory(read))) > 0)
            {
                read += n;
            }
        }
        catch (InvalidOperationException)
        {
            // 버퍼 한도를 넘는 큰 본문은 기록하지 않는다
            return null;
        }
        finally
        {
            request.Body.Position = 0;
        }

        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "외부 API 처리 중 오류: {Path}")]
    private static partial void LogFailed(ILogger logger, PathString path, Exception ex);

    /// <summary>응답을 그대로 내보내면서 앞부분만 복사해 두는 스트림.</summary>
    private sealed class CaptureStream(Stream inner, int limit) : Stream
    {
        private readonly MemoryStream _copy = new();

        public string Preview() => Encoding.UTF8.GetString(_copy.GetBuffer(), 0, (int)_copy.Length);

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Remember(buffer);
            inner.Write(buffer);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Remember(buffer.Span);
            await inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        private void Remember(ReadOnlySpan<byte> buffer)
        {
            var room = limit - (int)_copy.Length;
            if (room > 0)
            {
                _copy.Write(buffer[..Math.Min(room, buffer.Length)]);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _copy.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
