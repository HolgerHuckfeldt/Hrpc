using MessagePack;
using System.Text.Json.Serialization;

namespace HrpcCommon.Models;

/// <summary>
/// Represents the response of an RPC call.
/// </summary>
[MessagePackObject]
public class HrpcResponse
{
    [Key(0)]
    public int Code { get; set; }

    [Key(1)]
    public string? Data { get; set; }

    [Key(2)]
    public byte[]? Content { get; set; }

    /// <summary>
    /// Stream for large binary responses (avoid buffering into byte[]).
    /// When set, transports should stream directly instead of serializing Content.
    /// Not serialized by MessagePack/JSON (transient, transport-only).
    /// </summary>
    [IgnoreMember]
    [JsonIgnore]
    public Stream? ContentStream { get; set; }

    [Key(3)]
    public string? Id { get; set; }

    /// <summary>
    /// Resolved dependencies (alias → value) for sequential request chaining.
    /// </summary>
    [Key(4)]
    public Dictionary<string, string>? ExposedDependencies { get; set; }

    /// <summary>
    /// Structured error details, populated when Code != 200.
    /// </summary>
    [Key(5)]
    [JsonPropertyName("error")]
    public HrpcError? Error { get; set; }

    /// <summary>
    /// Returns true if this response represents a successful call (Code 200-299).
    /// </summary>
    [JsonIgnore]
    [IgnoreMember]
    public bool IsSuccess => Code is >= 200 and <= 299;
}
