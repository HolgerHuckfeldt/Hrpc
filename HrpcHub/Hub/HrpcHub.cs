using HrpcCore.Model.Handler;
using HrpcCore.Model.Messages;
using Microsoft.AspNetCore.SignalR;

namespace HrpcHub.Hub
{

    public class HrpcHub(HrpcCore.Services.IHrpcCore service) : Microsoft.AspNetCore.SignalR.Hub
    {
        public async Task<HrpcResponse?> DoWork(HrpcRequest request)
        {
            var user = Context.UserIdentifier;
            return await service.InvokeDi(request, Context.GetHttpContext(), Context.ConnectionAborted);
        }

        public async Task<IEnumerable<HrpcResponse>> DoWorkMany(HrpcMultiRequest? request)
        {
            if (request == null)
            {
                return new List<HrpcResponse>();
            }
            if (request.Requests == null)
            {
                return new List<HrpcResponse>();
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
