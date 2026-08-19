using System.Text.Json.Serialization;

namespace OutWit.Controller.Visualization.ParaView.Validation;

/// <summary>
/// JSON shape of an allowlist artifact (<c>Allowlists/paraview-&lt;major.minor&gt;.json</c>).
/// </summary>
internal sealed class ParaViewProxyAllowlistDocument
{
    #region Properties

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("paraview")]
    public string? ParaView { get; set; }

    [JsonPropertyName("origin")]
    public string? Origin { get; set; }

    [JsonPropertyName("proxies")]
    public List<string>? Proxies { get; set; }

    [JsonPropertyName("pluginProxies")]
    public Dictionary<string, List<string>>? PluginProxies { get; set; }

    #endregion
}
