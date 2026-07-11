using WaevaRpcCore.Services;
using Microsoft.AspNetCore.SignalR;

namespace WaevaRpcHub.Hub
{

    public class WaevaRpcHub(WaevaRpcCore.Services.IWaevaRpcCore service) : Microsoft.AspNetCore.SignalR.Hub
    {
        public async Task<WaevaRpcResponse?> DoWork(WaevaRpcRequest request)
        {
            var user = Context.UserIdentifier;
            return await service.InvokeDi(request, Context.GetHttpContext(), Context.ConnectionAborted);
        }

        public async Task<IEnumerable<WaevaRpcResponse>> DoWorkMany(WaevaRpcMultiRequest? request)
        {
            if (request == null)
            {
                return new List<WaevaRpcResponse>();
            }
            if (request.Requests == null)
            {
                return new List<WaevaRpcResponse>();
            }

            var result = await service.InvokeDi(
                request.Requests, 
                Context.GetHttpContext(),
                request.Mode, 
                Context.ConnectionAborted);
            return result;
        }
    }
}
