using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WaevaRpcClient.WaevaRpc
{
    public class WaevaRpcRestJsonClient : WaevaRpcClientBase, IWaevaRpcClient, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _serverBase;
        private readonly string _apiPath;
        private readonly bool _ownsHttpClient;

        public WaevaRpcRestJsonClient(string serverBaseUrl, HttpClient? httpClient = null, string apiPath = "api/waeva",
            TimeSpan? httpClientTimeout = null)
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

            if (httpClientTimeout.HasValue)
                _httpClient.Timeout = httpClientTimeout.Value;

            _serverBase = serverBaseUrl.EndsWith("/", StringComparison.Ordinal) ? serverBaseUrl : serverBaseUrl + "/";
            _apiPath = apiPath.Trim('/');
        }

        public override async Task<WaevaRpcResponse?> Call(WaevaRpcRequest? request, CancellationToken ct = default)
        {
            if (request == null)
            {
                return null;
            }

            var jsonData = JsonSerializer.Serialize(request, JsonOptions);
            var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_serverBase}{_apiPath}/json", content, ct);

            // Bytes statt String lesen — WaevaRpcResponseParser macht EINEN Pass über die
            // Wire-Bytes (ID, Envelope, T zusammen) statt drei Parses mit JsonDocument-Baum.
            var responseBytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorText = Encoding.UTF8.GetString(responseBytes);
                return new WaevaRpcResponse
                {
                    Code = (int)response.StatusCode,
                    Id = request.Id,
                    Error = new WaevaRpcError
                    {
                        Code = (int)response.StatusCode,
                        Message = $"HTTP Error: {response.StatusCode}",
                        Details = errorText
                    }
                };
            }
            return WaevaRpcResponseParser.Parse(responseBytes);
        }

        public override async Task<IEnumerable<WaevaRpcResponse?>?> Call(WaevaRpcMultiRequest? request, CancellationToken ct = default)
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
            var responseBytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorText = Encoding.UTF8.GetString(responseBytes);
                return new List<WaevaRpcResponse>
                {
                    new WaevaRpcResponse
                    {
                        Code = (int)response.StatusCode,
                        Id = request.Requests?.FirstOrDefault()?.Id,
                        Error = new WaevaRpcError
                        {
                            Code = (int)response.StatusCode,
                            Message = $"HTTP Error: {response.StatusCode}",
                            Details = errorText
                        }
                    }
                };
            }
            // Batch = JSON-Array → ParseArray (ein Pass, DataBytes pro Element).
            var waevaRpcResponses = WaevaRpcResponseParser.ParseArray(responseBytes);
            return waevaRpcResponses ?? new List<WaevaRpcResponse?>();
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
