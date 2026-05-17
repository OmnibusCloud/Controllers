namespace OutWit.Controller.Render.Utils;

/// <summary>
/// Internal render backend diagnostics collected from the local Blender runtime.
/// </summary>
internal sealed class RenderDeviceDiagnostics
{
    #region Properties

    public string[] AvailableRenderBackends { get; set; } = [];

    public RenderDevice? SelectedRenderBackend { get; set; }

    public bool UsesGpuForRendering { get; set; }

    public string? SelectionMessage { get; set; }

    #endregion
}
