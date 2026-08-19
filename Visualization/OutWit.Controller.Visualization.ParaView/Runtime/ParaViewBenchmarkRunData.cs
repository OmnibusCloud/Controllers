using System.Text.Json;
using System.Text.Json.Serialization;

namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// The status document the embedded benchmark runner (<c>benchmark_frames.py</c>) writes: how many
/// frames of which size it rendered in how many seconds, on which render window, with which ParaView.
/// </summary>
public sealed class ParaViewBenchmarkRunData
{
    #region Constants

    private static readonly JsonSerializerOptions JSON_OPTIONS = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    #endregion

    #region Functions

    /// <summary>
    /// Reads the status document, or returns null when it is absent or unparsable (the caller
    /// reports the runner's exit code and stderr tail instead).
    /// </summary>
    /// <param name="path">Status file path.</param>
    /// <returns>The parsed document or null.</returns>
    public static ParaViewBenchmarkRunData? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            return JsonSerializer.Deserialize<ParaViewBenchmarkRunData>(File.ReadAllText(path), JSON_OPTIONS);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Throughput in output pixels per second: the unit the render-frame work estimate is expressed in
    /// (pixels + materialized bytes / 64), so the grid allocator compares like with like.
    /// </summary>
    /// <returns>Pixels per second, or 0 when nothing was measured.</returns>
    public double ComputeRate()
    {
        if (Frames <= 0 || RenderSeconds <= 0 || Width <= 0 || Height <= 0)
            return 0;

        return (double)Frames * Width * Height / RenderSeconds;
    }

    #endregion

    #region Properties

    [JsonPropertyName("schema")]
    public int Schema { get; set; }

    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("stage")]
    public string Stage { get; set; } = string.Empty;

    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;

    [JsonPropertyName("frames")]
    public int Frames { get; set; }

    [JsonPropertyName("render_seconds")]
    public double RenderSeconds { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("points")]
    public long Points { get; set; }

    [JsonPropertyName("render_window")]
    public string RenderWindow { get; set; } = string.Empty;

    [JsonPropertyName("paraview_version")]
    public string ParaviewVersion { get; set; } = string.Empty;

    [JsonPropertyName("output_bytes")]
    public long OutputBytes { get; set; }

    #endregion
}
