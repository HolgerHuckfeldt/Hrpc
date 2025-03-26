using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HrpcClient.Hrpc
{
    public interface IHrpcClient
    {
        public Task<HrpcResponse?> Call(HrpcRequest request);
        public Task<T?> Call<T>(HrpcRequest? request);

        public Task<IEnumerable<HrpcResponse?>?> Call(HrpcMultiRequest? request);

    }
}
