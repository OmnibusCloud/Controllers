using MemoryPack;
using OutWit.Cloud.Documents;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Model;

/// <summary>
/// Preflight validation result for tiled still rendering on the current packaged runtime.
/// </summary>
[JobDocumentContract("render.preflightStillTiled@1")]
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only, and deploy the server before any client that writes a new member (default MemoryPack mode rejects payloads with unknown members).
public partial class RenderPreflightStillTiledData : ModelBase
{
    #region Properties

    /// <summary>
    /// Whether the current packaged runtime can execute the requested tiled still render.
    /// </summary>
    [MemoryPackOrder(0)]
    public bool CanRender { get; set; }

    /// <summary>
    /// Runtime target resolved for the current process, such as <c>windows-x64</c>.
    /// </summary>
    [MemoryPackOrder(1)]
    public string? RuntimeTarget { get; set; }

    /// <summary>
    /// Requested tile blend mode for the preflight validation.
    /// </summary>
    [MemoryPackOrder(2)]
    public TileBlendMode RequestedBlendMode { get; set; }

    /// <summary>
    /// Human-readable preflight issues that block the requested tiled render.
    /// </summary>
    [MemoryPackOrder(3)]
    public List<string> Issues { get; set; } = [];

    /// <summary>
    /// Human-readable preflight warnings that do not block the requested tiled render.
    /// </summary>
    [MemoryPackOrder(4)]
    public List<string> Warnings { get; set; } = [];

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not RenderPreflightStillTiledData other)
            return false;

        return CanRender.Is(other.CanRender)
               && RuntimeTarget.Is(other.RuntimeTarget)
               && RequestedBlendMode == other.RequestedBlendMode
               && Issues.Is(other.Issues)
               && Warnings.Is(other.Warnings);
    }

    public override ModelBase Clone()
    {
        return new RenderPreflightStillTiledData
        {
            CanRender = CanRender,
            RuntimeTarget = RuntimeTarget,
            RequestedBlendMode = RequestedBlendMode,
            Issues = [.. Issues],
            Warnings = [.. Warnings]
        };
    }

    #endregion
}
