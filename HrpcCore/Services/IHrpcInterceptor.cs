using HrpcCommon.Models;
using Microsoft.AspNetCore.Http;

namespace HrpcCore.Services;

/// <summary>
/// Delegate that invokes the next interceptor or the actual RPC method.
/// </summary>
public delegate Task<HrpcResponse?> HrpcInvocationDelegate(HrpcRequest request, CancellationToken ct);

/// <summary>
/// Interface for interceptors that can intercept RPC calls before/after execution.
/// Use for logging, tracing, caching, validation, metrics, etc.
/// </summary>
public interface IHrpcInterceptor
{
    /// <summary>
    /// Intercepts an RPC call. Call <paramref name="next"/> to continue the pipeline.
    /// </summary>
    Task<HrpcResponse?> InvokeAsync(
        HrpcRequest request,
        HrpcInvocationDelegate next,
        CancellationToken ct);
}

/// <summary>
/// Context information passed to interceptors for additional metadata.
/// </summary>
public class HrpcInvocationContext
{
    public required HrpcRequest Request { get; init; }
    public HttpContext? HttpContext { get; init; }
    public string ControllerName => Request.Controller;
    public string MethodName => Request.Method;
    public string RequestId => Request.Id ?? string.Empty;
}