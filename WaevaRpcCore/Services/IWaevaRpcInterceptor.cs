using WaevaRpcCommon.Models;
using Microsoft.AspNetCore.Http;

namespace WaevaRpcCore.Services;

/// <summary>
/// Delegate that invokes the next interceptor or the actual RPC method.
/// </summary>
public delegate Task<WaevaRpcResponse?> WaevaRpcInvocationDelegate(WaevaRpcRequest request, CancellationToken ct);

/// <summary>
/// Interface for interceptors that can intercept RPC calls before/after execution.
/// Use for logging, tracing, caching, validation, metrics, etc.
/// </summary>
public interface IWaevaRpcInterceptor
{
    /// <summary>
    /// Intercepts an RPC call. Call <paramref name="next"/> to continue the pipeline.
    /// </summary>
    Task<WaevaRpcResponse?> InvokeAsync(
        WaevaRpcRequest request,
        WaevaRpcInvocationDelegate next,
        CancellationToken ct);
}

/// <summary>
/// Context information passed to interceptors for additional metadata.
/// </summary>
public class WaevaRpcInvocationContext
{
    public required WaevaRpcRequest Request { get; init; }
    public HttpContext? HttpContext { get; init; }
    public string ControllerName => Request.Controller;
    public string MethodName => Request.Method;
    public string RequestId => Request.Id ?? string.Empty;
}