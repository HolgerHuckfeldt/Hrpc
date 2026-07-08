using HrpcCommon.Models;

namespace HrpcCommon.Exceptions;

/// <summary>
/// Unified exception for all Hrpc transport and invocation errors.
/// </summary>
public class HrpcException : Exception
{
    /// <summary>
    /// Structured error details, if available.
    /// </summary>
    public HrpcError? Error { get; }

    public HrpcException(Exception innerException)
        : base(innerException.Message, innerException)
    {
    }

    public HrpcException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    public HrpcException(HrpcError error, Exception? innerException = null)
        : base(error.Message, innerException)
    {
        Error = error;
    }
}
