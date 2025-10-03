using System.Text.Json;
using System.Threading;
using HrpcClient.Hrpc.Exceptions;

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
            if (response is { Code: 200 } && !string.IsNullOrEmpty(response.Data))
            {
                try
                {
                    return JsonSerializer.Deserialize<T>(response.Data, JsonOptions);
                }
                catch (Exception ex)
                {
                    throw new HrpcException("Fehler bei der Deserialisierung der Antwort.", ex);
                }
            }
            if (response != null && response.Code != 200)
            {
                throw new HrpcException($"Server antwortete mit Fehlercode {response.Code}: {response.Data}", null);
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
