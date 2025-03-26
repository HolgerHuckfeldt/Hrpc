using HrpcCore.Model.Handler;
using HrpcCore.Model.Messages.Mex;
using HrpcCore.Model.Messages;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HrpcCore.Services
{
    public interface IHrpcCore
    {
        void Register<T>();
        void Register(Type handler);

        /// <summary>
        /// Nimmt mehrere Requests entgegen und ruft sie nacheinander auf.
        /// </summary>
        Task<IEnumerable<HrpcResponse?>> InvokeDi(
            IEnumerable<HrpcRequest> requests,
            HttpContext? context,
            ExecutionMode mode = ExecutionMode.Parallel,
            CancellationToken ct = default);

        /// <summary>
        /// Nimmt einen einzelnen Request entgegen und führt ihn aus.
        /// </summary>
        Task<HrpcResponse?> InvokeDi(
            HrpcRequest request,
            HttpContext? context,
            CancellationToken ct = default);

        DiscoveryInfo GetDiscoveryInfo();
    }
}
