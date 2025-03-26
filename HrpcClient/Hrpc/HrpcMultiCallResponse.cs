using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HrpcClient.Hrpc
{
    public class HrpcMultiCallResponse
    {
        private readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions()
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

        public T? Get<T>(string name)
        {

            var theResult = Responses.FirstOrDefault(s => s.Id == name);
            if (theResult != null)
            {
                if (theResult.Code == 200)
                    return JsonSerializer.Deserialize<T>(theResult.Data, _jsonSerializerOptions);
            }

            return default;
        }
    }
}
