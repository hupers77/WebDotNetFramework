using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Portal.Core.Security;

namespace Portal.Framework.Web.Api.Outbound;

/// <summary>송신 요청.</summary>
/// <param name="TargetKey">등록한 대상 키 (<c>TB_API_OUTBOUND.TARGET_KEY</c>).</param>
/// <param name="Method">HTTP 메서드.</param>
/// <param name="Path">대상 기본 주소 뒤에 붙는 경로(쿼리 포함). 절대 주소나 <c>..</c>은 허용하지 않는다.</param>
/// <param name="JsonBody">JSON 본문 (없으면 null).</param>
/// <param name="Idempotent">같은 요청을 다시 보내도 결과가 같다고 보장하면 true (POST도 재시도). GET·HEAD·PUT·DELETE는 기본으로 재시도한다.</param>
public sealed record OutboundRequest(string TargetKey, HttpMethod Method, string Path, string? JsonBody = null, bool Idempotent = false);

/// <summary>송신 응답. 본문은 <see cref="OutboundApiClient.MaxResponseBytes"/>까지만 읽는다.</summary>
/// <param name="StatusCode">HTTP 상태 코드 (4xx·5xx도 그대로 돌려준다).</param>
/// <param name="Body">본문 (글자).</param>
/// <param name="Truncated">본문이 잘렸는지.</param>
/// <param name="Attempts">시도 횟수.</param>
public sealed record OutboundResponse(int StatusCode, string Body, bool Truncated, int Attempts)
{
    /// <summary>2xx 여부.</summary>
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}

/// <summary>송신 실패 종류.</summary>
public enum OutboundFailure
{
    /// <summary>등록되지 않은 대상 키.</summary>
    UnknownTarget,

    /// <summary>사용 중지된 대상.</summary>
    Disabled,

    /// <summary>잘못된 경로 (대상 밖으로 나가는 주소).</summary>
    InvalidPath,

    /// <summary>연속 실패로 잠시 차단 중.</summary>
    CircuitOpen,

    /// <summary>제한 시간 초과.</summary>
    Timeout,

    /// <summary>연결·전송 실패 (허용되지 않은 주소 포함).</summary>
    Network,

    /// <summary>비밀값을 복호화할 수 없음 (키 분실 등).</summary>
    SecretUnreadable,
}

/// <summary>송신을 할 수 없거나 응답을 못 받았을 때. 메시지에는 주소·비밀값을 담지 않는다.</summary>
public sealed class OutboundApiException(OutboundFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>실패 종류.</summary>
    public OutboundFailure Failure { get; } = failure;
}

/// <summary>
/// 외부 시스템 호출. 코드에는 주소를 쓰지 않고 등록한 대상 키만 쓴다(설계서 11장).
/// 인증·제한 시간·재시도는 등록값을 따르고, 허용되지 않은 주소(내부망·루프백 등)와 리디렉트는 막으며, 모든 호출을 <c>LOG.TB_API_LOG</c>에 방향 OUT으로 남긴다.
/// </summary>
public interface IOutboundApiClient
{
    /// <summary>요청을 보낸다.</summary>
    /// <exception cref="OutboundApiException">대상이 없거나 막혔거나 응답을 받지 못했을 때.</exception>
    Task<OutboundResponse> SendAsync(OutboundRequest request, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class OutboundApiClient(
    IOutboundTargetStore targets,
    IHttpClientFactory httpClients,
    ISecretProtector secrets,
    ApiLogBuffer logs,
    OutboundCircuitBreaker breaker,
    TimeProvider time) : IOutboundApiClient
{
    /// <summary>외부 호출용 HttpClient 이름 (외부 주소만).</summary>
    public const string PublicClientName = "portal-outbound";

    /// <summary>사내망 허용 대상용 HttpClient 이름.</summary>
    public const string PrivateClientName = "portal-outbound-private";

    /// <summary>읽는 응답 본문 최대 크기.</summary>
    internal const int MaxResponseBytes = 1024 * 1024;

    private static readonly TimeSpan BackoffBase = TimeSpan.FromMilliseconds(200);

    /// <inheritdoc />
    public async Task<OutboundResponse> SendAsync(OutboundRequest request, CancellationToken cancellationToken = default)
    {
        var started = time.GetTimestamp();
        var startedAt = time.GetUtcNow().UtcDateTime;
        var target = await targets.GetAsync(request.TargetKey, cancellationToken)
            ?? throw new OutboundApiException(OutboundFailure.UnknownTarget, "등록되지 않은 송신 대상입니다.");
        if (!target.Active)
        {
            throw new OutboundApiException(OutboundFailure.Disabled, "사용 중지된 송신 대상입니다.");
        }

        var uri = BuildUri(target, request.Path);
        if (breaker.IsOpen(target.TargetKey))
        {
            throw new OutboundApiException(OutboundFailure.CircuitOpen, "연속 실패로 잠시 호출을 멈췄습니다.");
        }

        var secret = target.AuthSecretEnc is null ? null : Unprotect(target.AuthSecretEnc);
        var retriable = request.Idempotent || request.Method == HttpMethod.Get || request.Method == HttpMethod.Head
            || request.Method == HttpMethod.Put || request.Method == HttpMethod.Delete || request.Method == HttpMethod.Options;
        var attempts = retriable ? target.RetryCount + 1 : 1;
        var client = httpClients.CreateClient(target.AllowPrivate ? PrivateClientName : PublicClientName);

        OutboundResponse? response = null;
        OutboundApiException? failure = null;
        var made = 0;
        try
        {
            while (made < attempts)
            {
                made++;
                try
                {
                    response = await SendOnceAsync(client, target, secret, uri, request, made, cancellationToken);
                    failure = null;
                    if (!IsRetryableStatus(response.StatusCode))
                    {
                        break;
                    }
                }
                catch (OutboundApiException ex) when (ex.Failure is OutboundFailure.Timeout or OutboundFailure.Network)
                {
                    failure = ex;
                }

                if (made < attempts)
                {
                    await Task.Delay(BackoffBase * Math.Pow(2, made - 1), time, cancellationToken);
                }
            }

            if (failure is not null)
            {
                breaker.RecordFailure(target.TargetKey);
                throw failure;
            }

            if (IsRetryableStatus(response!.StatusCode))
            {
                breaker.RecordFailure(target.TargetKey);
            }
            else
            {
                breaker.RecordSuccess(target.TargetKey);
            }

            return response with { Attempts = made };
        }
        finally
        {
            logs.Enqueue(new ApiLogEntry
            {
                LogDt = startedAt,
                DirectionCd = "OUT",
                ClientId = target.TargetKey,
                HttpMethod = request.Method.Method.Length > 10 ? request.Method.Method[..10] : request.Method.Method,
                Url = ApiLogMasker.MaskUrl(uri.GetLeftPart(UriPartial.Path), uri.Query),
                StatusCode = response is null ? null : (short)response.StatusCode,
                ElapsedMs = (int)time.GetElapsedTime(started).TotalMilliseconds,
                RequestBody = ApiLogMasker.MaskBody(request.JsonBody, "application/json"),
                ResponseBody = response is null ? null : ApiLogMasker.MaskBody(response.Body, "application/json"),
                TraceId = Activity.Current?.Id,
            });
        }
    }

    internal static bool IsRetryableStatus(int status) => status >= 500 || status is 408 or 429;

    /// <summary>기본 주소 뒤에 경로를 붙인다. 호스트·포트·방식이 바뀌거나 상위 경로로 나가면 거부한다.</summary>
    internal static Uri BuildUri(OutboundTarget target, string path)
    {
        if (path is null || path.Contains("://", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)
            || path.Contains('\\', StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal)
            || path.Any(char.IsControl))
        {
            throw new OutboundApiException(OutboundFailure.InvalidPath, "경로가 올바르지 않습니다.");
        }

        var joined = target.BaseUrl + (path.Length == 0 || path[0] is '/' or '?' ? path : "/" + path);
        var baseUri = new Uri(target.BaseUrl);
        if (!Uri.TryCreate(joined, UriKind.Absolute, out var uri)
            || uri.Scheme != baseUri.Scheme || uri.Host != baseUri.Host || uri.Port != baseUri.Port || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new OutboundApiException(OutboundFailure.InvalidPath, "경로가 올바르지 않습니다.");
        }

        return uri;
    }

    private string Unprotect(string protectedText)
    {
        try
        {
            return secrets.Unprotect(protectedText);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
        {
            throw new OutboundApiException(OutboundFailure.SecretUnreadable, "인증 비밀값을 읽을 수 없습니다. 송신 대상 화면에서 다시 입력하세요.", ex);
        }
    }

    private static async Task<OutboundResponse> SendOnceAsync(HttpClient client, OutboundTarget target, string? secret, Uri uri, OutboundRequest request, int attempt, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(request.Method, uri);
        ApplyAuth(message, target, secret);
        if (request.JsonBody is not null)
        {
            message.Content = new StringContent(request.JsonBody, Encoding.UTF8, "application/json");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(target.TimeoutSec));
        try
        {
            using var http = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var (body, truncated) = await ReadBodyAsync(http, timeout.Token);
            return new OutboundResponse((int)http.StatusCode, body, truncated, attempt);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OutboundApiException(OutboundFailure.Timeout, "송신 대상이 제한 시간 안에 응답하지 않았습니다.");
        }
        catch (HttpRequestException ex)
        {
            throw new OutboundApiException(OutboundFailure.Network, "송신 대상에 연결하지 못했습니다.", ex);
        }
    }

    private static void ApplyAuth(HttpRequestMessage message, OutboundTarget target, string? secret)
    {
        switch (target.AuthTypeCd)
        {
            case "BASIC" when secret is not null:
                message.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{target.AuthUser}:{secret}")));
                break;
            case "BEARER" when secret is not null:
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
                break;
            case "API_KEY" when secret is not null && target.AuthHeader is not null:
                message.Headers.TryAddWithoutValidation(target.AuthHeader, secret);
                break;
        }
    }

    private static async Task<(string Body, bool Truncated)> ReadBodyAsync(HttpResponseMessage http, CancellationToken cancellationToken)
    {
        await using var stream = await http.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
            {
                buffer.Write(chunk, 0, (int)(MaxResponseBytes - buffer.Length));
                return (Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length), true);
            }

            buffer.Write(chunk, 0, read);
        }

        return (Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length), false);
    }

    /// <summary>주소를 풀어 허용된 주소로만 연결한다 (DNS가 내부 주소를 돌려주는 경우 포함).</summary>
    internal static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, bool allowPrivate, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        var allowed = addresses.Where(a => OutboundAddressPolicy.IsAllowed(a, allowPrivate)).ToArray();
        if (allowed.Length == 0)
        {
            throw new HttpRequestException("허용되지 않은 주소입니다.");
        }

        Exception? last = null;
        foreach (var address in allowed)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                last = ex;
            }
        }

        throw new HttpRequestException("연결하지 못했습니다.", last);
    }
}
