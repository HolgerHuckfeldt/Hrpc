using HrpcCommon.Models;

namespace Hrpc.Spike.LinqProvider;

/// <summary>
/// Sammelt mehrere typsichere <see cref="HrpcCallSpec"/>-Objekte zu einem Batch.
/// Die Reihenfolge bleibt erhalten; <see cref="Dep{T}"/>-Verdrahtungen stecken
/// bereits in den Specs (Alias + dependencyMapping), daher reicht es, sie
/// gesammelt als Multi-Request zu senden. Der Server wählt den topologischen
/// Batch-Pfad automatisch (anhand der dependencyMappings).
/// </summary>
public sealed class HrpcBatch
{
    private readonly List<HrpcCallSpec> _specs = new();
    public IReadOnlyList<HrpcCallSpec> Specs => _specs;

    public ExecutionMode Mode { get; set; } = ExecutionMode.Serial;

    public HrpcBatch() { }

    public HrpcBatch(params HrpcCallSpec[] specs) => _specs.AddRange(specs);

    public HrpcBatch Add(HrpcCallSpec spec)
    {
        if (spec == null) throw new ArgumentNullException(nameof(spec));
        _specs.Add(spec);
        return this;
    }

    public HrpcMultiRequest ToMultiRequest() => new()
    {
        Mode = Mode,
        Requests = _specs.Select(s => s.ToRequest()).ToList()
    };
}