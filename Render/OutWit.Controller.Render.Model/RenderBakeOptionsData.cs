using MemoryPack;
using OutWit.Cloud.Documents;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Model;

/// <summary>
/// Options for a host-delegated simulation bake (Render.BakeSimulation). The bake runs once on a single
/// node selected by Grid.Delegate (the fastest compatible node, by the same render-throughput benchmark
/// used for distribution), converting an unbaked sequential simulation into a per-frame, frame-addressable
/// OpenVDB cache that is then sliced and rendered distributed.
/// </summary>
[JobDocumentContract("render.bakeOptions@1")]
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only, and deploy the server before any client that writes a new member (default MemoryPack mode rejects payloads with unknown members).
public partial class RenderBakeOptionsData : ModelBase
{
    #region Properties

    /// <summary>
    /// Comma-separated simulation kinds to bake. v1 honours "Fluid" (Mantaflow gas/liquid domains).
    /// Other kinds (Cloth, Particles, …) reuse the same delegate-bake framework in later iterations.
    /// </summary>
    [MemoryPackOrder(0)]
    public string SimulationKinds { get; set; } = "Fluid";

    /// <summary>
    /// Cache format for the baked sequence. v1 produces "OpenVDB" (per-frame, frame-addressable).
    /// </summary>
    [MemoryPackOrder(1)]
    public string CacheFormat { get; set; } = "OpenVDB";

    /// <summary>
    /// Optional override for the fluid domain resolution (resolution_max). 0 keeps the scene's value.
    /// Lets a submitter cap network bake cost / cache size without editing the scene.
    /// </summary>
    [MemoryPackOrder(2)]
    public int ResolutionMax { get; set; }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not RenderBakeOptionsData other)
            return false;

        return SimulationKinds.Is(other.SimulationKinds)
               && CacheFormat.Is(other.CacheFormat)
               && ResolutionMax == other.ResolutionMax;
    }

    public override ModelBase Clone()
    {
        return new RenderBakeOptionsData
        {
            SimulationKinds = SimulationKinds,
            CacheFormat = CacheFormat,
            ResolutionMax = ResolutionMax
        };
    }

    #endregion
}
