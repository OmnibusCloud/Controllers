using MemoryPack;
using OutWit.Cloud.Documents;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Model;

/// <summary>
/// Unified preflight validation result across the currently supported render modes.
/// </summary>
[JobDocumentContract("render.preflight@1")]
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only, and deploy the server before any client that writes a new member (default MemoryPack mode rejects payloads with unknown members).
public partial class RenderPreflightData : ModelBase
{
    #region Properties

    /// <summary>
    /// Packaged runtime diagnostics used to evaluate the preflight request.
    /// </summary>
    [MemoryPackOrder(0)]
    public RenderRuntimeDiagnosticsData? RuntimeDiagnostics { get; set; }

    /// <summary>
    /// Single-frame still preflight result, evaluated as a one-frame specialization of frame rendering.
    /// </summary>
    [MemoryPackOrder(1)]
    public RenderPreflightFramesData? Still { get; set; }

    /// <summary>
    /// Frame-range render preflight result.
    /// </summary>
    [MemoryPackOrder(2)]
    public RenderPreflightFramesData? Frames { get; set; }

    /// <summary>
    /// Tiled-still render preflight result.
    /// </summary>
    [MemoryPackOrder(3)]
    public RenderPreflightStillTiledData? StillTiled { get; set; }

    /// <summary>
    /// Video render preflight result.
    /// </summary>
    [MemoryPackOrder(4)]
    public RenderPreflightVideoData? Video { get; set; }

    /// <summary>
    /// Whether all currently evaluated render modes are ready on the current packaged runtime.
    /// </summary>
    [MemoryPackOrder(5)]
    public bool CanRenderAll { get; set; }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not RenderPreflightData other)
            return false;

        return RuntimeDiagnostics.Is(other.RuntimeDiagnostics)
               && Still.Is(other.Still)
               && Frames.Is(other.Frames)
               && StillTiled.Is(other.StillTiled)
               && Video.Is(other.Video)
               && CanRenderAll.Is(other.CanRenderAll);
    }

    public override ModelBase Clone()
    {
        return new RenderPreflightData
        {
            RuntimeDiagnostics = (RenderRuntimeDiagnosticsData?)RuntimeDiagnostics?.Clone(),
            Still = (RenderPreflightFramesData?)Still?.Clone(),
            Frames = (RenderPreflightFramesData?)Frames?.Clone(),
            StillTiled = (RenderPreflightStillTiledData?)StillTiled?.Clone(),
            Video = (RenderPreflightVideoData?)Video?.Clone(),
            CanRenderAll = CanRenderAll
        };
    }

    #endregion
}
