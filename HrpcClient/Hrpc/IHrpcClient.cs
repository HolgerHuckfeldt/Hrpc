using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HrpcClient.Hrpc
{
    public interface IHrpcClient
    {
        Task<HrpcResponse?> Call(HrpcRequest request, CancellationToken ct = default);
        Task<T?> Call<T>(HrpcRequest? request, CancellationToken ct = default);
        Task<IEnumerable<HrpcResponse?>?> Call(HrpcMultiRequest? request, CancellationToken ct = default);
    }
}
