using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WaevaRpcClient.WaevaRpc
{
    public interface IWaevaRpcClient
    {
        Task<WaevaRpcResponse?> Call(WaevaRpcRequest request, CancellationToken ct = default);
        Task<T?> Call<T>(WaevaRpcRequest? request, CancellationToken ct = default);
        Task<IEnumerable<WaevaRpcResponse?>?> Call(WaevaRpcMultiRequest? request, CancellationToken ct = default);
        /// <summary>
        /// Sendet einen WaevaRpcRequest und liefert das binäre <c>Content</c>-Feld
        /// (z. B. für <c>byte[]</c>-Rückgaben). Wirft <c>WaevaRpcException</c> bei
        /// nicht-erfolgreichem Call.
        /// </summary>
        Task<byte[]?> CallBinary(WaevaRpcRequest? request, CancellationToken ct = default);
    }
}
