using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HrpcClient.Hrpc
{
    public class HrpcRestJsonClient(string serverBaseUrl) : HrpcClientBase, IHrpcClient
    {
        private readonly HttpClient _httpClient = new();

        // Falls kein Slash am Ende, anhängen, damit das Handling beim Aufruf einfacher wird
        private readonly string _serverBase = serverBaseUrl.EndsWith("/")
            ? serverBaseUrl
            : serverBaseUrl + "/";

        /// <summary>
        /// Führt einen einzelnen RPC-Call aus und gibt das HrpcResponse zurück.
        /// </summary>
        public override async Task<HrpcResponse?> Call(HrpcRequest? request)
        {
            if (request == null)
            {
                return null;
            }

            // 1) Request als JSON serialisieren
            var jsonData = JsonSerializer.Serialize(request, JsonOptions);

            // 2) POST an "api/Hrpc" -> Single-Endpoint
            var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_serverBase}api/Hrpc/json", content);

            // 3) Falls Server-Fehler => Return HrpcResponse mit passendem Code
            if (!response.IsSuccessStatusCode)
            {
                var cont = await response.Content.ReadAsStringAsync();
                return new HrpcResponse
                {
                    Code = (int)response.StatusCode,
                    Data = $"HTTP Error: {response.StatusCode}"
                };
            }

            // 4) Antwort als JSON einlesen
            var responseJson = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<HrpcResponse>(responseJson, JsonOptions);
        }

        /// <summary>
        /// Mehrere RPC-Requests hintereinander (Batch) als JSON versenden.
        /// </summary>
        public override async Task<IEnumerable<HrpcResponse?>?> Call(HrpcMultiRequest? request)
        {

            if (request == null)
                return null;
            if (request.Requests == null)
                return null;

            foreach (var requestRequest in request.Requests)
            {
                if (string.IsNullOrEmpty(requestRequest.Id))
                {
                    requestRequest.Id = $"{requestRequest.Controller}.{requestRequest.Method}";
                }
            }

            // 1) Requests in JSON serialisieren
            var jsonData = JsonSerializer.Serialize(request, JsonOptions);

            // 2) POST an "api/Hrpc/multi"
            var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_serverBase}api/Hrpc/json/multi", content);

            if (!response.IsSuccessStatusCode)
            {
                var res = await response.Content.ReadAsStringAsync();
                // Fehler
                return new List<HrpcResponse>
                    {
                        new HrpcResponse
                        {
                            Code = (int)response.StatusCode,
                            Data = $"HTTP Error: {response.StatusCode}"
                        }
                    };
            }

            // 3) JSON-Response -> List<HrpcResponse>
            var responseJson = await response.Content.ReadAsStringAsync();
            var hrpcResponses = JsonSerializer.Deserialize<List<HrpcResponse>>(responseJson, JsonOptions);
            return hrpcResponses ?? new List<HrpcResponse>();
        }
    }
}
