using System.Text.Json.Serialization;

namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// The status document the GPU probe (<c>gpu_probe.py</c>) writes: which render window class and
/// which OpenGL renderer one probe render actually got.
/// </summary>
public sealed class ParaViewGpuProbeStatus
{
    #region Properties

    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("stage")]
    public string Stage { get; set; } = string.Empty;

    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;

    [JsonPropertyName("render_window")]
    public string RenderWindow { get; set; } = string.Empty;

    [JsonPropertyName("renderer")]
    public string Renderer { get; set; } = string.Empty;

    #endregion
}
