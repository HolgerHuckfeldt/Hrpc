using WaevaRpcCommon.Models;

namespace WaevaRpc.Spike.LinqProvider;

/// <summary>
/// Sammelt mehrere typsichere <see cref="WaevaRpcCallSpec"/>-Objekte zu einem Batch.
/// Die Reihenfolge bleibt erhalten; <see cref="Dep{T}"/>-Verdrahtungen stecken
/// bereits in den Specs (Alias + dependencyMapping), daher reicht es, sie
/// gesammelt als Multi-Request zu senden. Der Server wählt den topologischen
/// Batch-Pfad automatisch (anhand der dependencyMappings).
/// </summary>
public sealed class WaevaRpcBatch
{
    private readonly List<WaevaRpcCallSpec> _specs = new();
    public IReadOnlyList<WaevaRpcCallSpec> Specs => _specs;

    public ExecutionMode Mode { get; set; } = ExecutionMode.Serial;

    public WaevaRpcBatch() { }

    public WaevaRpcBatch(params WaevaRpcCallSpec[] specs) => _specs.AddRange(specs);

    public WaevaRpcBatch Add(WaevaRpcCallSpec spec)
    {
        if (spec == null) throw new ArgumentNullException(nameof(spec));
        _specs.Add(spec);
        return this;
    }

    public WaevaRpcMultiRequest ToMultiRequest() => new()
    {
        Mode = Mode,
        Requests = _specs.Select(s => s.ToRequest()).ToList()
    };
}