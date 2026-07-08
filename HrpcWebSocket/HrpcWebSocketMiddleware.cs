using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HrpcCore.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace HrpcWebSocket;

/// <summary>
/// Middleware für einen schlanken, eigenen WebSocket-Transport für Hrpc.
/// Nutzt ausschließlich Standard-WebSockets (RFC 6455) und JSON, damit Clients in
/// Java, JavaScript, Python oder anderen Sprachen einfach integrierbar sind.
/// </summary>
public class HrpcWebSocketMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHrpcCore _hrpcCore;
    private readonly ILogger<HrpcWebSocketMiddleware>? _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public HrpcWebSocketMiddleware(
        RequestDelegate next,
        IHrpcCore hrpcCore,
        ILogger<HrpcWebSocketMiddleware>? logger = null)
    {
        _next = next;
        _hrpcCore = hrpcCore;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            await _next(context);
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        _logger?.LogInformation("WebSocket connection established: {ConnectionId}", context.Connection.Id);

        await HandleConnectionAsync(context, webSocket);
    }

    private const int MaxMessageSize = 1_048_576; // 1 MB

    private async Task HandleConnectionAsync(HttpContext context, WebSocket webSocket)
    {
        var buffer = new byte[1024 * 4];

        while (webSocket.State == WebSocketState.Open)
        {
            // Bytes sammeln (nicht pro Chunk dekodieren) — sonst korruptieren
            // Multi-Byte-Zeichen an Chunk-Grenzen (A2).
            using var messageStream = new MemoryStream();
            WebSocketReceiveResult result;

            do
            {
                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), context.RequestAborted);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Text && result.Count > 0)
                {
                    messageStream.Write(buffer, 0, result.Count);

                    if (messageStream.Length > MaxMessageSize)
                    {
                        await SendErrorAsync(webSocket, "Message too large.");
                        return;
                    }
                }
            }
            while (!result.EndOfMessage);

            if (messageStream.Length == 0)
                continue;

            var message = Encoding.UTF8.GetString(messageStream.GetBuffer(), 0, (int)messageStream.Length);
            if (string.IsNullOrWhiteSpace(message))
                continue;

            await ProcessMessageAsync(context, webSocket, message);
        }
    }

    private async Task ProcessMessageAsync(HttpContext context, WebSocket webSocket, string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;

            object? response;

            if (root.TryGetProperty("requests", out _) && root.TryGetProperty("mode", out _))
            {
                var multiRequest = JsonSerializer.Deserialize<HrpcMultiRequest>(message, _jsonOptions);
                if (multiRequest?.Requests == null)
                {
                    await SendErrorAsync(webSocket, "Invalid multi request.");
                    return;
                }

                response = await _hrpcCore.InvokeDi(multiRequest.Requests, context, multiRequest.Mode, context.RequestAborted);
            }
            else
            {
                var request = JsonSerializer.Deserialize<HrpcRequest>(message, _jsonOptions);
                if (request == null)
                {
                    await SendErrorAsync(webSocket, "Invalid request.");
                    return;
                }

                response = await _hrpcCore.InvokeDi(request, context, context.RequestAborted);
            }

            var json = JsonSerializer.Serialize(response, _jsonOptions);
            await SendTextAsync(webSocket, json);
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "Failed to parse WebSocket message as JSON.");
            await SendErrorAsync(webSocket, "Invalid JSON in request.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing WebSocket message.");
            await SendErrorAsync(webSocket, "Internal server error.");
        }
    }

    private static async Task SendTextAsync(WebSocket webSocket, string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cts.Token);
    }

    private async Task SendErrorAsync(WebSocket webSocket, string errorMessage)
    {
        var errorResponse = new
        {
            code = 400,
            data = errorMessage
        };

        await SendTextAsync(webSocket, JsonSerializer.Serialize(errorResponse, _jsonOptions));
    }
}
