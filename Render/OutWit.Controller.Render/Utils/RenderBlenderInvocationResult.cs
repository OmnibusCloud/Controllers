namespace OutWit.Controller.Render.Utils;

/// <summary>
/// Result of one Blender process invocation used by render fallback logic.
/// </summary>
internal sealed class RenderBlenderInvocationResult
{
    #region Properties

    public int ExitCode { get; set; }

    public string Stdout { get; set; } = string.Empty;

    public string Stderr { get; set; } = string.Empty;

    public RenderDevice? SelectedRenderBackend { get; set; }

    /// <summary>GPU backends Blender reported as available on this node (devices enumerated), regardless
    /// of which one was selected. Drives the smart device fallback's "try the next GPU backend" step.</summary>
    public IReadOnlyList<RenderDevice> AvailableGpuBackends { get; set; } = [];

    public string? SelectionMessage { get; set; }

    #endregion
}
