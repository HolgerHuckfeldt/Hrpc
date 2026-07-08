using System.Text.Json;

namespace HrpcClient.Hrpc
{
    public class HrpcMultiCallResponse
    {
        // Einmalige, wiederverwendete Options statt pro Instanz (D3).
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public ExecutionMode Mode { get; set; } = ExecutionMode.Serial;

        public List<HrpcResponse?>? Responses { get; set; }

        public static async Task<HrpcMultiCallResponse> Call(IHrpcClient client, HrpcMultiRequest request)
        {
            var response = await client.Call(request);
            return new HrpcMultiCallResponse()
            {
                Responses = response?.ToList()
            };
        }

        /// <summary>
        /// Liefert das Ergebnis des per <paramref name="name"/> (Request-Id) benannten
        /// Sub-Calls deserialisiert in <typeparamref name="T"/>. Liefert <c>default</c>
        /// bei nicht-erfolgreichem Sub-Call oder fehlendem <c>Data</c>; siehe
        /// <see cref="GetError"/> für die Fehlerdetails.
        /// </summary>
        public T? Get<T>(string name)
        {
            var theResult = Responses?.FirstOrDefault(s => s?.Id == name);
            if (theResult is not { IsSuccess: true })
                return default;

            if (string.IsNullOrEmpty(theResult.Data))
                return default;

            return JsonSerializer.Deserialize<T>(theResult.Data, JsonOptions);
        }

        /// <summary>
        /// Liefert die strukturierten Fehlerdetails des per <paramref name="name"/>
        /// benannten Sub-Calls oder <c>null</c>, wenn der Sub-Call erfolgreich war
        /// oder nicht gefunden wurde.
        /// </summary>
        public HrpcError? GetError(string name)
        {
            var theResult = Responses?.FirstOrDefault(s => s?.Id == name);
            if (theResult is null or { IsSuccess: true })
                return null;

            return theResult.Error ?? HrpcError.FromResponse(theResult);
        }
    }
}