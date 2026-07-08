using MessagePack;
using System.Text.Json.Serialization;

namespace HrpcCommon.Models;

/// <summary>
/// Unified error model for all Hrpc transports.
/// Carries structured error information across REST, SignalR, and WebSocket.
/// </summary>
[MessagePackObject]
public class HrpcError
{
    /// <summary>
    /// HTTP-like status code (e.g. 400, 401, 404, 500).
    /// </summary>
    [Key(0)]
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    [Key(1)]
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Additional error details (e.g. stack trace – only populated in Development).
    /// </summary>
    [Key(2)]
    [JsonPropertyName("details")]
    public string? Details { get; set; }

    /// <summary>
    /// Correlation ID matching the originating request.
    /// </summary>
    [Key(3)]
    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }

    /// <summary>
    /// Creates an HrpcError from an HrpcResponse (when Code != 200).
    /// </summary>
    public static HrpcError FromResponse(HrpcResponse response)
    {
        return new HrpcError
        {
            Code = response.Code,
            Message = response.Data ?? "Unknown error",
            RequestId = response.Id
        };
    }
}