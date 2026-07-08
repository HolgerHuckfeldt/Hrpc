using System.Text.Json;
using System.Threading;
// HrpcException is now provided via global using alias from GlobalUsings.cs

namespace HrpcClient.Hrpc
{
    public abstract class HrpcClientBase(JsonSerializerOptions? options = null) : IHrpcClient
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
        /// Sendet einen einzelnen HrpcRequest und liefert eine HrpcResponse zurück.
        /// Die konkrete Umsetzung übernimmt die abgeleitete Client-Klasse (z. B. REST oder SignalR).
        /// </summary>
        public abstract Task<HrpcResponse?> Call(HrpcRequest? request, CancellationToken ct = default);

        /// <summary>
        /// Sendet einen HrpcRequest und versucht, das Feld Data in den angegebenen Typ T zu deserialisieren.
        /// Wirft eine HrpcException, wenn ein Fehler (z. B. Netzwerkfehler oder Deserialisierungsfehler) auftritt.
        /// </summary>
        public async Task<T?> Call<T>(HrpcRequest? request, CancellationToken ct = default)
        {
            if(request == null)
                return default;

            var response = await Call(request, ct);
            if (response is { IsSuccess: true } && !string.IsNullOrEmpty(response.Data))
            {
                try
                {
                    return JsonSerializer.Deserialize<T>(response.Data, JsonOptions);
                }
                catch (Exception ex)
                {
                    throw new HrpcException("Failed to deserialize the response.", ex);
                }
            }
            if (response != null && !response.IsSuccess)
            {
                var error = response.Error ?? HrpcError.FromResponse(response);
                throw new HrpcException(error, null);
            }
            return default;
        }

        /// <summary>
        /// Sendet einen HrpcRequest und liefert das binäre <c>Content</c>-Feld der
        /// Antwort (z. B. für Methoden, die <c>byte[]</c> zurückgeben). Wirft eine
        /// <see cref="HrpcException"/> bei nicht-erfolgreichem Call.
        /// </summary>
        public async Task<byte[]?> CallBinary(HrpcRequest? request, CancellationToken ct = default)
        {
            if (request == null)
                return default;

            var response = await Call(request, ct);
            if (response is { IsSuccess: true })
                return response.Content;

            if (response != null && !response.IsSuccess)
            {
                var error = response.Error ?? HrpcError.FromResponse(response);
                throw new HrpcException(error, null);
            }
            return default;
        }

        /// <summary>
        /// Sendet mehrere HrpcRequests (MultiCall) und liefert eine Liste von HrpcResponses zurück.
        /// Die konkrete Umsetzung übernimmt die abgeleitete Client-Klasse.
        /// </summary>
        public abstract Task<IEnumerable<HrpcResponse?>?> Call(HrpcMultiRequest? request, CancellationToken ct = default);
    }
}
