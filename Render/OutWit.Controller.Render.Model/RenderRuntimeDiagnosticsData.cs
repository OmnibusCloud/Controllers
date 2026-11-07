using MemoryPack;
using OutWit.Cloud.Documents;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Model;

/// <summary>
/// Runtime diagnostics for the packaged render controller tools available on the current node.
/// </summary>
[JobDocumentContract("render.runtimeDiagnostics@1")]
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only, and deploy the server before any client that writes a new member (default MemoryPack mode rejects payloads with unknown members).
public partial class RenderRuntimeDiagnosticsData : ModelBase
{
    #region Properties

    /// <summary>
    /// Runtime target resolved for the current process, such as <c>windows-x64</c>.
    /// </summary>
    [MemoryPackOrder(0)]
    public string? RuntimeTarget { get; set; }

    /// <summary>
    /// Whether the packaged Blender runtime is available.
    /// </summary>
    [MemoryPackOrder(1)]
    public bool BlenderAvailable { get; set; }

    /// <summary>
    /// Blender version string when available.
    /// </summary>
    [MemoryPackOrder(2)]
    public string? BlenderVersion { get; set; }

    /// <summary>
    /// Render backends detected as available for automatic local selection.
    /// </summary>
    [MemoryPackOrder(3)]
    public string[] AvailableRenderBackends { get; set; } = [];

    /// <summary>
    /// Backend currently selected by the node for automatic rendering.
    /// </summary>
    [MemoryPackOrder(4)]
    public string? SelectedRenderBackend { get; set; }

    /// <summary>
    /// Whether automatic local selection resolved to a GPU backend.
    /// </summary>
    [MemoryPackOrder(5)]
    public bool UsesGpuForRendering { get; set; }

    /// <summary>
    /// Human-readable automatic backend selection summary.
    /// </summary>
    [MemoryPackOrder(6)]
    public string? RenderBackendSelectionMessage { get; set; }

    /// <summary>
    /// Whether the packaged ffmpeg runtime is available.
    /// </summary>
    [MemoryPackOrder(7)]
    public bool FfmpegAvailable { get; set; }

    /// <summary>
    /// ffmpeg version string when available.
    /// </summary>
    [MemoryPackOrder(8)]
    public string? FfmpegVersion { get; set; }

    /// <summary>
    /// Whether the packaged ffprobe runtime is available.
    /// </summary>
    [MemoryPackOrder(9)]
    public bool FfprobeAvailable { get; set; }

    /// <summary>
    /// ffprobe version string when available.
    /// </summary>
    [MemoryPackOrder(10)]
    public string? FfprobeVersion { get; set; }

    /// <summary>
    /// Whether tiled center-priority crop stitching is supported.
    /// </summary>
    [MemoryPackOrder(11)]
    public bool SupportsCenterPriorityCrop { get; set; }

    /// <summary>
    /// Whether tiled alpha-blend stitching is supported.
    /// </summary>
    [MemoryPackOrder(12)]
    public bool SupportsAlphaBlend { get; set; }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not RenderRuntimeDiagnosticsData other)
            return false;

        return RuntimeTarget.Is(other.RuntimeTarget)
               && BlenderAvailable.Is(other.BlenderAvailable)
               && BlenderVersion.Is(other.BlenderVersion)
               && AvailableRenderBackends.SequenceEqual(other.AvailableRenderBackends)
               && SelectedRenderBackend.Is(other.SelectedRenderBackend)
               && UsesGpuForRendering.Is(other.UsesGpuForRendering)
               && RenderBackendSelectionMessage.Is(other.RenderBackendSelectionMessage)
               && FfmpegAvailable.Is(other.FfmpegAvailable)
               && FfmpegVersion.Is(other.FfmpegVersion)
               && FfprobeAvailable.Is(other.FfprobeAvailable)
               && FfprobeVersion.Is(other.FfprobeVersion)
               && SupportsCenterPriorityCrop.Is(other.SupportsCenterPriorityCrop)
               && SupportsAlphaBlend.Is(other.SupportsAlphaBlend);
    }

    public override ModelBase Clone()
    {
        return new RenderRuntimeDiagnosticsData
        {
            RuntimeTarget = RuntimeTarget,
            BlenderAvailable = BlenderAvailable,
            BlenderVersion = BlenderVersion,
            AvailableRenderBackends = AvailableRenderBackends.ToArray(),
            SelectedRenderBackend = SelectedRenderBackend,
            UsesGpuForRendering = UsesGpuForRendering,
            RenderBackendSelectionMessage = RenderBackendSelectionMessage,
            FfmpegAvailable = FfmpegAvailable,
            FfmpegVersion = FfmpegVersion,
            FfprobeAvailable = FfprobeAvailable,
            FfprobeVersion = FfprobeVersion,
            SupportsCenterPriorityCrop = SupportsCenterPriorityCrop,
            SupportsAlphaBlend = SupportsAlphaBlend
        };
    }

    #endregion
}
