using MessagePack;

namespace HrpcCommon.Models;

/// <summary>
/// Batch request container for multiple RPC calls.
/// </summary>
[MessagePackObject]
public class HrpcMultiRequest
{
    [Key(0)]
    public List<HrpcRequest>? Requests { get; set; }

    [Key(1)]
    public ExecutionMode Mode { get; set; } = ExecutionMode.Serial;
}