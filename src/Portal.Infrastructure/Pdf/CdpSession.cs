using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Portal.Infrastructure.Pdf;

/// <summary>
/// 브라우저 개발자 도구 프로토콜(CDP) 최소 클라이언트. 명령을 하나씩 보내고 같은 id의 응답이 올 때까지 기다린다(이벤트는 버린다).
/// </summary>
internal sealed class CdpSession(ClientWebSocket socket) : IAsyncDisposable
{
    private int nextId;

    /// <summary>브라우저 웹소켓 주소에 연결한다.</summary>
    internal static async Task<CdpSession> ConnectAsync(Uri browserUrl, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        try
        {
            socket.Options.Proxy = null; // 시스템 프록시를 타지 않고 로컬 포트에 바로 붙는다
            await socket.ConnectAsync(browserUrl, cancellationToken);
            return new CdpSession(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>명령을 보내고 결과(<c>result</c>)를 돌려준다. 오류 응답이면 예외.</summary>
    internal async Task<JsonObject> SendAsync(string method, object? parameters, string? sessionId, CancellationToken cancellationToken)
    {
        var id = ++nextId;
        var message = new JsonObject { ["id"] = id, ["method"] = method };
        if (parameters is not null)
        {
            message["params"] = JsonSerializer.SerializeToNode(parameters);
        }

        if (sessionId is not null)
        {
            message["sessionId"] = sessionId;
        }

        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message), WebSocketMessageType.Text, true, cancellationToken);

        while (true)
        {
            var reply = await ReceiveAsync(cancellationToken);
            if (reply["id"]?.GetValue<int>() != id)
            {
                continue;
            }

            if (reply["error"] is JsonObject error)
            {
                throw new InvalidOperationException($"PDF_BROWSER_ERROR: {method}: {error["message"]}");
            }

            return reply["result"] as JsonObject ?? [];
        }
    }

    private async Task<JsonObject> ReceiveAsync(CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var result = await socket.ReceiveAsync(chunk, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("PDF_BROWSER_CLOSED: 브라우저가 연결을 끊었습니다.");
            }

            buffer.Write(chunk, 0, result.Count);
            if (result.EndOfMessage)
            {
                buffer.Position = 0;
                return JsonNode.Parse(buffer)?.AsObject() ?? [];
            }
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
