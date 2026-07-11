using WaevaRpcCommon.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace WaevaRpcCore.Services;

/// <summary>
/// Built-in logging interceptor that traces RPC call durations.
/// </summary>
public class WaevaRpcLoggingInterceptor(ILogger<WaevaRpcLoggingInterceptor> logger) : IWaevaRpcInterceptor
{
    public async Task<WaevaRpcResponse?> InvokeAsync(
        WaevaRpcRequest request,
        WaevaRpcInvocationDelegate next,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        logger.LogTrace("Starting RPC call {Controller}.{Method} [{RequestId}]",
            request.Controller, request.Method, request.Id);

        try
        {
            var response = await next(request, ct);
            stopwatch.Stop();
            logger.LogDebug("RPC call {Controller}.{Method} completed in {Duration}ms [{StatusCode}]",
                request.Controller, request.Method, stopwatch.ElapsedMilliseconds, response?.Code);
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex, "RPC call {Controller}.{Method} failed after {Duration}ms",
                request.Controller, request.Method, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}