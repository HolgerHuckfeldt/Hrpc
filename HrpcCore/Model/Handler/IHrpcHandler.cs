namespace HrpcCore.Model.Handler
{
    public interface IHrpcHandler
    {

        public string Name { get; }
        public Task<HrpcResponse> DoWork(HrpcRequest request);
    }
}
