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
        private readonly string _apiPath;
        private readonly bool _ownsHttpClient;

        public HrpcRestJsonClient(string serverBaseUrl, HttpClient? httpClient = null, string apiPath = "api/hrpc")
        {
            if (string.IsNullOrWhiteSpace(serverBaseUrl))
                throw new ArgumentException("Server-URL darf nicht leer sein.", nameof(serverBaseUrl));

            if (httpClient != null)
            {
                _httpClient = httpClient;
                _ownsHttpClient = false;
            }
            else
            {
                _httpClient = new HttpClient(
                    new SocketsHttpHandler
                    {
                        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                    },
                    disposeHandler: true);
                _ownsHttpClient = true;
            }

            _serverBase = serverBaseUrl.EndsWith("/", StringComparison.Ordinal) ? serverBaseUrl : serverBaseUrl + "/";
            _apiPath = apiPath.Trim('/');
        }

        public override async Task<HrpcResponse?> Call(HrpcRequest? request, CancellationToken ct = default)
        {
            if (request == null)
            {
                return null;
            }

            var jsonData = JsonSerializer.Serialize(request, JsonOptions);
            var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_serverBase}{_apiPath}/json", content, ct);

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                return new HrpcResponse
                {
                    Code = (int)response.StatusCode,
                    Data = $"HTTP Error: {response.StatusCode} | {responseJson}",
                    Error = new HrpcError
                    {
                        Code = (int)response.StatusCode,
                        Message = $"HTTP Error: {response.StatusCode}",
                        Details = responseJson
                    }
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
            var response = await _httpClient.PostAsync($"{_serverBase}{_apiPath}/json/multi", content, ct);
            var responseJson = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                return new List<HrpcResponse>
                {
                    new HrpcResponse
                    {
                        Code = (int)response.StatusCode,
                        Data = $"HTTP Error: {response.StatusCode} | {responseJson}",
                        Error = new HrpcError
                        {
                            Code = (int)response.StatusCode,
                            Message = $"HTTP Error: {response.StatusCode}",
                            Details = responseJson
                        }
                    }
                };
            }
            var hrpcResponses = JsonSerializer.Deserialize<List<HrpcResponse>>(responseJson, JsonOptions);
            return hrpcResponses ?? new List<HrpcResponse>();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && _ownsHttpClient)
            {
                _httpClient?.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
