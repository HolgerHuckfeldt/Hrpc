using MessagePack;

namespace WaevaRpcCommon.Models;

/// <summary>
/// Batch request container for multiple RPC calls.
/// </summary>
[MessagePackObject]
public class WaevaRpcMultiRequest
{
    [Key(0)]
    public List<WaevaRpcRequest>? Requests { get; set; }

    [Key(1)]
    public ExecutionMode Mode { get; set; } = ExecutionMode.Serial;
}