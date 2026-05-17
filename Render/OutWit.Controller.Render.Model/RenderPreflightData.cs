using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Model;

/// <summary>
/// Unified preflight validation result across the currently supported render modes.
/// </summary>
[MemoryPackable]
public partial class RenderPreflightData : ModelBase
{
    #region Properties

    /// <summary>
    /// Packaged runtime diagnostics used to evaluate the preflight request.
    /// </summary>
    public RenderRuntimeDiagnosticsData? RuntimeDiagnostics { get; set; }

    /// <summary>
    /// Single-frame still preflight result, evaluated as a one-frame specialization of frame rendering.
    /// </summary>
    public RenderPreflightFramesData? Still { get; set; }

    /// <summary>
    /// Frame-range render preflight result.
    /// </summary>
    public RenderPreflightFramesData? Frames { get; set; }

    /// <summary>
    /// Tiled-still render preflight result.
    /// </summary>
    public RenderPreflightStillTiledData? StillTiled { get; set; }

    /// <summary>
    /// Video render preflight result.
    /// </summary>
    public RenderPreflightVideoData? Video { get; set; }

    /// <summary>
    /// Whether all currently evaluated render modes are ready on the current packaged runtime.
    /// </summary>
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
