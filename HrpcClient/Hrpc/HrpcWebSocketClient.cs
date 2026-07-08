using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace HrpcClient.Hrpc;

/// <summary>
/// Hrpc-Client über reine WebSockets (RFC 6455) mit ID-basierter
/// Request/Response-Korrelation für parallele Calls.
/// </summary>
public class HrpcWebSocketClient : HrpcClientBase, IHrpcClient, IAsyncDisposable
{
    private readonly ClientWebSocket _webSocket;
    private readonly Uri _uri;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ConcurrentDictionary<string, ITcsHolder> _pendingRequests = new();
    private CancellationTokenSource? _readerCts;
    private Task? _readerTask;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private int _requestCounter;

    /// <summary>
    /// Non-generic interface to store TaskCompletionSource instances of different types.
    /// </summary>
    private interface ITcsHolder
    {
        void SetResult(string json);
        void SetException(Exception ex);
        void SetCanceled();
    }

    private class TcsHolder<T> : ITcsHolder
    {
        public readonly TaskCompletionSource<T> Tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void SetResult(string json)
        {
            var result = JsonSerializer.Deserialize<T>(json);
            Tcs.TrySetResult(result!);
        }

        public void SetException(Exception ex) => Tcs.TrySetException(ex);
        public void SetCanceled() => Tcs.TrySetCanceled();
    }

    public HrpcWebSocketClient(string serverBaseUrl, ClientWebSocket? webSocket = null)
    {
        if (string.IsNullOrWhiteSpace(serverBaseUrl))
            throw new ArgumentException("Server-URL darf nicht leer sein.", nameof(serverBaseUrl));

        var baseUrl = serverBaseUrl.TrimEnd('/');
        var wsScheme = baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
        var hostAndPath = baseUrl.Substring(baseUrl.IndexOf("://", StringComparison.Ordinal) + 3);
        _uri = new Uri($"{wsScheme}://{hostAndPath}/hrpcws");

        _webSocket = webSocket ?? new ClientWebSocket();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// Stellt die WebSocket-Verbindung her und startet den Hintergrund-Reader.
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_webSocket.State == WebSocketState.Open)
            return;

        if (_webSocket.State == WebSocketState.Closed || _webSocket.State == WebSocketState.Aborted)
        {
            throw new InvalidOperationException("WebSocket wurde bereits geschlossen. Erstellen Sie eine neue Instanz.");
        }

        await _webSocket.ConnectAsync(_uri, ct);

        // Start background reader
        _readerCts = new CancellationTokenSource();
        _readerTask = Task.Run(() => ReadLoopAsync(_readerCts.Token), CancellationToken.None);
    }

    public override async Task<HrpcResponse?> Call(HrpcRequest? request, CancellationToken ct = default)
    {
        if (request == null)
            return null;

        await EnsureConnectedAsync(ct);
        return await SendAndAwaitResponseAsync<HrpcResponse?>(request, ct);
    }

    public override async Task<IEnumerable<HrpcResponse?>?> Call(HrpcMultiRequest? request, CancellationToken ct = default)
    {
        if (request == null)
            return null;

        await EnsureConnectedAsync(ct);
        return await SendAndAwaitResponseAsync<List<HrpcResponse?>>(request, ct);
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_webSocket.State != WebSocketState.Open)
            await ConnectAsync(ct);
    }

    private string NextId()
    {
        var n = Interlocked.Increment(ref _requestCounter);
        return $"ws-{n}-{Guid.NewGuid():N}";
    }

    private async Task<T?> SendAndAwaitResponseAsync<T>(object payload, CancellationToken ct)
    {
        // Assign a unique ID to the request for correlation
        string requestId;
        if (payload is HrpcRequest hr)
        {
            requestId = hr.Id ?? NextId();
            hr.Id = requestId;
        }
        else if (payload is HrpcMultiRequest mr)
        {
            requestId = mr.Requests?.FirstOrDefault()?.Id ?? NextId();
        }
        else
        {
            requestId = NextId();
        }

        var holder = new TcsHolder<T>();
        _pendingRequests[requestId] = holder;

        try
        {
            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);

            // Only serialize sends — receives are handled by the background reader
            await _sendLock.WaitAsync(ct);
            try
            {
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    ct);
            }
            finally
            {
                _sendLock.Release();
            }

            // Wait for the matching response
            using var reg = ct.Register(() => holder.SetCanceled());
            return await holder.Tcs.Task;
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
        }
    }

    /// <summary>
    /// Background loop that reads all WebSocket messages and dispatches
    /// them to the correct pending request by matching the response ID.
    /// </summary>
    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[1024 * 4];
        var builder = new StringBuilder();

        try
        {
            while (_webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                builder.Clear();
                WebSocketReceiveResult result;

                do
                {
                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Ack", CancellationToken.None);
                        CancelAllPending(new WebSocketException("WebSocket connection closed by server."));
                        return;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                }
                while (!result.EndOfMessage);

                var message = builder.ToString();
                if (string.IsNullOrWhiteSpace(message))
                    continue;

                DispatchResponse(message);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            CancelAllPending(ex);
        }
    }

    /// <summary>
    /// Parses the response JSON, extracts the ID, and completes the matching TCS.
    /// </summary>
    private void DispatchResponse(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            string? responseId = null;
            if (root.TryGetProperty("id", out var idProp))
            {
                responseId = idProp.GetString();
            }

            // Try to match by ID first
            if (responseId != null && _pendingRequests.TryGetValue(responseId, out var holder))
            {
                holder.SetResult(message);
                return;
            }

            // Fallback: if the response is an array (batch), try matching by first item's ID
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var first = root[0];
                if (first.TryGetProperty("id", out var firstIdProp))
                {
                    var firstId = firstIdProp.GetString();
                    if (firstId != null && _pendingRequests.TryGetValue(firstId, out var batchHolder))
                    {
                        batchHolder.SetResult(message);
                        return;
                    }
                }
            }

            // Last resort: deliver to the oldest pending request
            var oldest = _pendingRequests.OrderBy(kv => kv.Key).FirstOrDefault();
            if (!oldest.Equals(default(KeyValuePair<string, ITcsHolder>)))
            {
                _pendingRequests.TryRemove(oldest.Key, out _);
                oldest.Value.SetResult(message);
            }
        }
        catch (JsonException ex)
        {
            CancelAllPending(ex);
        }
    }

    private void CancelAllPending(Exception ex)
    {
        foreach (var kv in _pendingRequests)
        {
            kv.Value.SetException(ex);
        }
        _pendingRequests.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        _readerCts?.Cancel();

        if (_readerTask != null)
        {
            try { await _readerTask; } catch { /* ignore */ }
        }

        CancelAllPending(new ObjectDisposedException(nameof(HrpcWebSocketClient)));

        if (_webSocket.State == WebSocketState.Open)
        {
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
        }

        _webSocket.Dispose();
        _sendLock.Dispose();
        _readerCts?.Dispose();
    }
}
