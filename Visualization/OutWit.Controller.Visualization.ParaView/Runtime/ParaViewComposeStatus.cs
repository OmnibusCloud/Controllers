using System.Text.Json;
using System.Text.Json.Serialization;

namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// The bounded status document the composer script writes on every exit path (snake_case keys,
/// mirrored by <c>compose_scene.py</c>): the stage reached, the error text, the runtime and reader
/// versions, the timeline the saved state carries, the arrays the data offered and the colouring
/// and fit actually applied.
/// </summary>
public sealed class ParaViewComposeStatus
{
    #region Constants

    /// <summary>File name of the status document inside the workspace.</summary>
    public const string FILE_NAME = "compose_status.json";

    private const int MAX_STATUS_BYTES = 256 * 1024;

    private static readonly JsonSerializerOptions JSON_OPTIONS = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    #endregion

    #region Functions

    /// <summary>
    /// Reads a status document.
    /// </summary>
    /// <param name="path">Status file path.</param>
    /// <returns>The status, or null when the file is absent, oversized or malformed.</returns>
    public static ParaViewComposeStatus? TryRead(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0 || info.Length > MAX_STATUS_BYTES)
                return null;

            return JsonSerializer.Deserialize<ParaViewComposeStatus>(File.ReadAllText(path), JSON_OPTIONS);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Serializes a status document (the fake composer and tests).
    /// </summary>
    /// <returns>snake_case JSON.</returns>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JSON_OPTIONS);
    }

    #endregion

    #region Properties

    /// <summary>Status document contract version.</summary>
    public int Schema { get; set; }

    /// <summary>Whether the composition completed.</summary>
    public bool Ok { get; set; }

    /// <summary>The stage reached.</summary>
    public string Stage { get; set; } = string.Empty;

    /// <summary>The error text of a failed composition.</summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>The ParaView version that composed the state.</summary>
    public string ParaviewVersion { get; set; } = string.Empty;

    /// <summary>The bundled reader's version.</summary>
    public string ReaderVersion { get; set; } = string.Empty;

    /// <summary>The timeline the saved state's TimeKeeper carries (empty for static data).</summary>
    public List<double> TimestepValues { get; set; } = [];

    /// <summary>The point arrays the data offered.</summary>
    public List<string> PointArrays { get; set; } = [];

    /// <summary>The cell arrays the data offered.</summary>
    public List<string> CellArrays { get; set; } = [];

    /// <summary>The array the scene colours by (empty: solid colour).</summary>
    public string ColorArray { get; set; } = string.Empty;

    /// <summary>The association of <see cref="ColorArray"/>.</summary>
    public string ColorAssociation { get; set; } = string.Empty;

    /// <summary>The colour range baked into the state (min, max) or empty.</summary>
    public List<double> ColorRange { get; set; } = [];

    /// <summary>The data bounds the camera was fitted to (xmin, xmax, ymin, ymax, zmin, zmax) or empty.</summary>
    public List<double> Bounds { get; set; } = [];

    /// <summary>How many timesteps the fit inspected.</summary>
    public int FitSamples { get; set; }

    /// <summary>Size of the saved state in bytes.</summary>
    public long StateBytes { get; set; }

    /// <summary>Wall time of the composition inside the process.</summary>
    public double ComposeSeconds { get; set; }

    #endregion
}
