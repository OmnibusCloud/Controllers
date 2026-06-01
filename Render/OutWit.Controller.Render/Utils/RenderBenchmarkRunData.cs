using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Utils;

/// <summary>
/// Result of one node render benchmark run produced by
/// <see cref="BlenderRunner.RunBenchmarkRenderAsync"/>. Carries the in-process
/// render-only timing (spawn / scene-load / GPU-init excluded) plus the device
/// that was actually used, so the caller can compute a faithful throughput rate
/// and record the device for transparency.
/// </summary>
internal sealed class RenderBenchmarkRunData
{
    #region Properties

    /// <summary>Engine that was benchmarked.</summary>
    public RenderEngine Engine { get; init; }

    /// <summary>Samples per frame used for the run.</summary>
    public int Samples { get; init; }

    /// <summary>Render width in pixels.</summary>
    public int ResolutionX { get; init; }

    /// <summary>Render height in pixels.</summary>
    public int ResolutionY { get; init; }

    /// <summary>Frames rendered inside the timed loop.</summary>
    public int FramesRendered { get; init; }

    /// <summary>Render-only seconds (timed inside Blender, excludes process/GPU init).</summary>
    public double RenderSeconds { get; init; }

    /// <summary>Backend Blender actually rendered on (OPTIX / CUDA / HIP / METAL / CPU / engine).</summary>
    public RenderDevice? SelectedRenderBackend { get; init; }

    /// <summary>Raw backend string reported by Blender (e.g. the actual engine id for Eevee/GP).</summary>
    public string? RawBackend { get; init; }

    /// <summary>GPU backends the runtime reported as available.</summary>
    public string[] AvailableRenderBackends { get; init; } = [];

    /// <summary>Human-readable backend-selection message.</summary>
    public string? SelectionMessage { get; init; }

    /// <summary>True when the selected backend is a GPU backend.</summary>
    public bool UsesGpu { get; init; }

    #endregion

    #region Functions

    /// <summary>
    /// Throughput in pixel-samples per render-second:
    /// <c>resX·resY·samples·frames / renderSeconds</c>. This is the value the Grid
    /// allocator consumes as the node's render <c>Rate</c>.
    /// </summary>
    public double ComputeRate()
    {
        if (RenderSeconds <= 0)
            return 0;

        return (double)ResolutionX * ResolutionY * Samples * FramesRendered / RenderSeconds;
    }

    #endregion
}
