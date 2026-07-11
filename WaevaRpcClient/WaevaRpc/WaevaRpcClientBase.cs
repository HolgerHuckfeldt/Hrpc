using System.Text.Json;
using System.Threading;
// WaevaRpcException is now provided via global using alias from GlobalUsings.cs

namespace WaevaRpcClient.WaevaRpc
{
    public abstract class WaevaRpcClientBase(JsonSerializerOptions? options = null) : IWaevaRpcClient
    {
        /// <summary>
        /// Gemeinsame JSON-Serializer-Optionen, die von allen Clients genutzt werden.
        /// </summary>
        protected readonly JsonSerializerOptions JsonOptions = options ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        /// <summary>
        /// Sendet einen einzelnen WaevaRpcRequest und liefert eine WaevaRpcResponse zurück.
        /// Die konkrete Umsetzung übernimmt die abgeleitete Client-Klasse (z. B. REST oder SignalR).
        /// </summary>
        public abstract Task<WaevaRpcResponse?> Call(WaevaRpcRequest? request, CancellationToken ct = default);

        /// <summary>
        /// Sendet einen WaevaRpcRequest und versucht, das Feld Data in den angegebenen Typ T zu deserialisieren.
        /// Wirft eine WaevaRpcException, wenn ein Fehler (z. B. Netzwerkfehler oder Deserialisierungsfehler) auftritt.
        /// </summary>
        public async Task<T?> Call<T>(WaevaRpcRequest? request, CancellationToken ct = default)
        {
            if(request == null)
                return default;

            var response = await Call(request, ct);
            if (response is { IsSuccess: true })
            {
                // Single-Pass Fast-Path: DataBytes (rohe UTF-8-Bytes aus dem Transport-
                // Parser) direkt in T deserialisieren — ein Pass, kein JsonElement-Baum.
                if (response.DataBytes is { } dataBytes && dataBytes.Length > 0)
                {
                    try { return JsonSerializer.Deserialize<T>(dataBytes, JsonOptions); }
                    catch (Exception ex)
                    {
                        throw new WaevaRpcException("Failed to deserialize the response.", ex);
                    }
                }
                // Fallback: Data als JsonElement (SignalR-Client via MessagePack-Formatter,
                // Legacy-Server ohne Converter, oder leerer Erfolg ohne Ergebniswert).
                if (response.Data.HasValue)
                {
                    try { return response.Data.Value.Deserialize<T>(JsonOptions); }
                    catch (Exception ex)
                    {
                        throw new WaevaRpcException("Failed to deserialize the response.", ex);
                    }
                }
                return default;
            }
            if (response != null && !response.IsSuccess)
            {
                var error = response.Error ?? WaevaRpcError.FromResponse(response);
                throw new WaevaRpcException(error, null);
            }
            return default;
        }

        /// <summary>
        /// Sendet einen WaevaRpcRequest und liefert das binäre <c>Content</c>-Feld der
        /// Antwort (z. B. für Methoden, die <c>byte[]</c> zurückgeben). Wirft eine
        /// <see cref="WaevaRpcException"/> bei nicht-erfolgreichem Call.
        /// </summary>
        public async Task<byte[]?> CallBinary(WaevaRpcRequest? request, CancellationToken ct = default)
        {
            if (request == null)
                return default;

            var response = await Call(request, ct);
            if (response is { IsSuccess: true })
                return response.Content;

            if (response != null && !response.IsSuccess)
            {
                var error = response.Error ?? WaevaRpcError.FromResponse(response);
                throw new WaevaRpcException(error, null);
            }
            return default;
        }

        /// <summary>
        /// Sendet mehrere WaevaRpcRequests (MultiCall) und liefert eine Liste von WaevaRpcResponses zurück.
        /// Die konkrete Umsetzung übernimmt die abgeleitete Client-Klasse.
        /// </summary>
        public abstract Task<IEnumerable<WaevaRpcResponse?>?> Call(WaevaRpcMultiRequest? request, CancellationToken ct = default);
    }
}
