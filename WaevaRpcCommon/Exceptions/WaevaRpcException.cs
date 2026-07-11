using WaevaRpcCommon.Models;

namespace WaevaRpcCommon.Exceptions;

/// <summary>
/// Unified exception for all WaevaRpc transport and invocation errors.
/// </summary>
public class WaevaRpcException : Exception
{
    /// <summary>
    /// Structured error details, if available.
    /// </summary>
    public WaevaRpcError? Error { get; }

    public WaevaRpcException(Exception innerException)
        : base(innerException.Message, innerException)
    {
    }

    public WaevaRpcException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    public WaevaRpcException(WaevaRpcError error, Exception? innerException = null)
        : base(error.Message, innerException)
    {
        Error = error;
    }
}
