using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HrpcClient.Hrpc
{
    public class HrpcRestJsonClient : HrpcClientBase, IHrpcClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _serverBase;

        public HrpcRestJsonClient(string serverBaseUrl, HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            _serverBase = serverBaseUrl.EndsWith("/") ? serverBaseUrl : serverBaseUrl + "/";
        }

        public override async Task<HrpcResponse?> Call(HrpcRequest? request, CancellationToken ct = default)
        {
            if (request == null)
            {
                return null;
            }

            var jsonData = JsonSerializer.Serialize(request, JsonOptions);
            var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_serverBase}api/Hrpc/json", content, ct);

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                return new HrpcResponse
                {
                    Code = (int)response.StatusCode,
                    Data = $"HTTP Error: {response.StatusCode} | {responseJson}"
                };
            }
            return JsonSerializer.Deserialize<HrpcResponse>(responseJson, JsonOptions);
        }

        public override async Task<IEnumerable<HrpcResponse?>?> Call(HrpcMultiRequest? request, CancellationToken ct = default)
        {
            if (request == null || request.Requests == null)
                return null;

            foreach (var requestRequest in request.Requests)
            {
                if (string.IsNullOrEmpty(requestRequest.Id))
                {
                    requestRequest.Id = $"{requestRequest.Controller}.{requestRequest.Method}";
                }
            }

            var jsonData = JsonSerializer.Serialize(request, JsonOptions);
            var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_serverBase}api/Hrpc/json/multi", content, ct);
            var responseJson = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                return new List<HrpcResponse>
                {
                    new HrpcResponse
                    {
                        Code = (int)response.StatusCode,
                        Data = $"HTTP Error: {response.StatusCode} | {responseJson}"
                    }
                };
            }
            var hrpcResponses = JsonSerializer.Deserialize<List<HrpcResponse>>(responseJson, JsonOptions);
            return hrpcResponses ?? new List<HrpcResponse>();
        }
    }
}
