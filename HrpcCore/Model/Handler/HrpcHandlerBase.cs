using HrpcCore.Attributes;
using System.Net;

namespace HrpcCore.Model.Handler
{

    public abstract class HrpcHandlerBase : IHrpcHandler
    {
        public abstract Task<HrpcResponse> DoWork(HrpcRequest request);

        public abstract string Name { get; }

        protected HrpcResponse Ok(string stringContent)
        {
            return Ok(stringContent, null);
        }

        protected HrpcResponse Ok(string stringContent, byte[]? binaryContent)
        {
            return new HrpcResponse() { Code = 200, Data = stringContent, Content = binaryContent };
        }

        protected HrpcResponse BadRequest(string stringContent)
        {
            return new HrpcResponse() { Code = (int)HttpStatusCode.BadRequest, Data = stringContent, Content = null };
        }

        protected HrpcResponse InternalServerError(string stringContent="")
        {
            return new HrpcResponse() { Code = (int)HttpStatusCode.InternalServerError, Data = stringContent, Content = null };
        }

        protected HrpcResponse NotFound(string stringContent="")
        {
            return new HrpcResponse() { Code = (int)HttpStatusCode.NotFound, Data = stringContent, Content = null };
        }

        protected HrpcResponse NoContent(string stringContent="")
        {
            return new HrpcResponse() { Code = (int)HttpStatusCode.NoContent, Data = stringContent, Content = null };
        }


    }
}
