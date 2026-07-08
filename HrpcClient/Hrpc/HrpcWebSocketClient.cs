using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace HrpcClient.Hrpc;

/// <summary>
/// Hrpc-Client über reine WebSockets (RFC 6455) mit ID-basierter
/// Request/Response-Korrelation für parallele Calls.
/// </summary>
public class HrpcWebSocketClient : HrpcClientBase, IHrpcClient, IAsyncDisposable
{
    private readonly ClientWebSocket _webSocket;
    private readonly Uri _uri;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly ConcurrentDictionary<string, ITcsHolder> _pendingRequests = new();
    private readonly ILogger<HrpcWebSocketClient>? _logger;
    private CancellationTokenSource? _readerCts;
    private Task? _readerTask;
    private int _requestCounter;
    private readonly TimeSpan? _callTimeout;

    /// <summary>
    /// Non-generic interface to store TaskCompletionSource instances of different types.
    /// </summary>
    private interface ITcsHolder
    {
        void SetResult(string json);
        void SetException(Exception ex);
        void SetCanceled();
    }

    private class TcsHolder<T>(JsonSerializerOptions options) : ITcsHolder
    {
        public readonly TaskCompletionSource<T> Tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly JsonSerializerOptions _options = options;

        public void SetResult(string json)
        {
            // Mit den transportweit einheitlichen (case-insensitiven) Options
            // deserialisieren — sonst binden camelCase-Server-Responses nicht.
            var result = JsonSerializer.Deserialize<T>(json, _options);
            Tcs.TrySetResult(result!);
        }

        public void SetException(Exception ex) => Tcs.TrySetException(ex);
        public void SetCanceled() => Tcs.SetCanceled();
    }

    public HrpcWebSocketClient(string serverBaseUrl, ClientWebSocket? webSocket = null,
        string? wsPath = "hrpcws", TimeSpan? callTimeout = null,
        ILogger<HrpcWebSocketClient>? logger = null)
        : base()
    {
        if (string.IsNullOrWhiteSpace(serverBaseUrl))
            throw new ArgumentException("Server-URL darf nicht leer sein.", nameof(serverBaseUrl));

        var baseUrl = serverBaseUrl.TrimEnd('/');
        var wsScheme = baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
        var hostAndPath = baseUrl.Substring(baseUrl.IndexOf("://", StringComparison.Ordinal) + 3);
        var path = (wsPath ?? "hrpcws").Trim('/');
        _uri = new Uri($"{wsScheme}://{hostAndPath}/{path}");

        _webSocket = webSocket ?? new ClientWebSocket();
        _callTimeout = callTimeout;
        _logger = logger;
    }

    /// <summary>
    /// Stellt die WebSocket-Verbindung her und startet den Hintergrund-Reader.
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_webSocket.State == WebSocketState.Open)
            return;

        if (_webSocket.State == WebSocketState.Closed || _webSocket.State == WebSocketState.Aborted)
            throw new InvalidOperationException("WebSocket wurde bereits geschlossen. Erstellen Sie eine neue Instanz.");

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
        if (_webSocket.State == WebSocketState.Open)
            return;

        // Connect-Race (B2): nur ein Caller darf verbinden, Rest wartet und sieht Open.
        await _connectLock.WaitAsync(ct);
        try
        {
            if (_webSocket.State == WebSocketState.Open)
                return;
            await ConnectAsync(ct);
        }
        finally
        {
            _connectLock.Release();
        }
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
            requestId = string.IsNullOrEmpty(hr.Id) ? NextId() : hr.Id;
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

        var holder = new TcsHolder<T>(JsonOptions);
        _pendingRequests[requestId] = holder;

        // Call-Timeout: verknüpftes CTS, das sowohl das Senden als auch das Warten abbricht.
        CancellationTokenSource? timeoutCts = null;
        CancellationTokenSource? linkedCts = null;
        try
        {
            var effectiveCt = ct;
            if (_callTimeout is { } timeout)
            {
                timeoutCts = new CancellationTokenSource(timeout);
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                effectiveCt = linkedCts.Token;
            }

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);

            // Only serialize sends — receives are handled by the background reader
            await _sendLock.WaitAsync(effectiveCt);
            try
            {
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    effectiveCt);
            }
            finally
            {
                _sendLock.Release();
            }

            // Wait for the matching response
            using var reg = effectiveCt.Register(() => holder.SetCanceled());
            return await holder.Tcs.Task;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Einheitliche Fehleroberfläche (C1): Transportfehler als HrpcException.
            throw new HrpcException("WebSocket transport error.", ex);
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
            linkedCts?.Dispose();
            timeoutCts?.Dispose();
        }
    }

    /// <summary>
    /// Background loop that reads all WebSocket messages and dispatches
    /// them to the correct pending request by matching the response ID.
    /// </summary>
    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 4);

        try
        {
            while (_webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                // Bytes sammeln (nicht pro Chunk dekodieren) — sonst korruptieren
                // Multi-Byte-Zeichen an Chunk-Grenzen (A2).
                using var messageBuffer = new MemoryStream();

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

                    if (result.MessageType == WebSocketMessageType.Text && result.Count > 0)
                    {
                        messageBuffer.Write(buffer, 0, result.Count);
                    }
                }
                while (!result.EndOfMessage);

                if (messageBuffer.Length == 0)
                    continue;

                var message = Encoding.UTF8.GetString(messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
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
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
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

            // Match by ID first
            if (responseId != null && _pendingRequests.TryGetValue(responseId, out var holder))
            {
                holder.SetResult(message);
                return;
            }

            // Array (batch): über das erste Element matchen (Server erhält pro-Request-Id nach v1).
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

            // Kein Match -> NICHT raten (B3: stille Fehlzuordnung). Verwerfen + loggen;
            // der wartende Caller bricht über sein CancellationToken ab.
            _logger?.LogWarning("Received WebSocket response with no matching pending request (id={Id}). Dropping.", responseId);
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
            try
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
            catch { /* ignore */ }
        }

        _webSocket.Dispose();
        _sendLock.Dispose();
        _connectLock.Dispose();
        _readerCts?.Dispose();
    }
}