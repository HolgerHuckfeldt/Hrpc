using WaevaRpcCore.Model.Messages.Mex;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WaevaRpcCore.Services
{
    public interface IWaevaRpcCore
    {
        void Register<T>();
        void Register(Type handler);

        /// <summary>
        /// Nimmt mehrere Requests entgegen und ruft sie nacheinander auf.
        /// </summary>
        Task<IEnumerable<WaevaRpcResponse?>> InvokeDi(
            IEnumerable<WaevaRpcRequest> requests,
            HttpContext? context,
            ExecutionMode mode = ExecutionMode.Parallel,
            CancellationToken ct = default);

        /// <summary>
        /// Nimmt einen einzelnen Request entgegen und führt ihn aus.
        /// </summary>
        Task<WaevaRpcResponse?> InvokeDi(
            WaevaRpcRequest request,
            HttpContext? context,
            CancellationToken ct = default);

        DiscoveryInfo GetDiscoveryInfo();
    }
}
