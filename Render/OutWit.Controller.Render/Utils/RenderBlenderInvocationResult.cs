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

    public string? SelectionMessage { get; set; }

    #endregion
}
