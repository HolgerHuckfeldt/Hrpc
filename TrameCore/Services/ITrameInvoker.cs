using TrameCore.Model.Messages.Mex;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TrameCore.Services
{
    public interface ITrameCore
    {
        void Register<T>();
        void Register(Type handler);

        /// <summary>
        /// Nimmt mehrere Requests entgegen und ruft sie nacheinander auf.
        /// </summary>
        Task<IEnumerable<TrameResponse?>> InvokeDi(
            IEnumerable<TrameRequest> requests,
            HttpContext? context,
            ExecutionMode mode = ExecutionMode.Parallel,
            CancellationToken ct = default);

        /// <summary>
        /// Nimmt einen einzelnen Request entgegen und führt ihn aus.
        /// </summary>
        Task<TrameResponse?> InvokeDi(
            TrameRequest request,
            HttpContext? context,
            CancellationToken ct = default);

        DiscoveryInfo GetDiscoveryInfo();
    }
}
