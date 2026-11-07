using System.Text.Json;
using System.Text.Json.Serialization;

namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// The task document the composer script (<c>compose_scene.py</c>) reads: absolute paths inside the
/// task workspace, the materialized data file and its logical path, and the presentation choices as
/// pvpython-facing tokens. Written by <see cref="ParaViewComposeExecutor"/> as snake_case JSON.
/// </summary>
public sealed class ParaViewComposeTask
{
    #region Constants

    /// <summary>Task file contract version.</summary>
    public const int SCHEMA_VERSION = 1;

    /// <summary>File name of the task document inside the workspace.</summary>
    public const string FILE_NAME = "compose.json";

    /// <summary>The composer's argument naming the task file.</summary>
    public const string TASK_FILE_ARGUMENT = "--task-file";

    private static readonly JsonSerializerOptions JSON_OPTIONS = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    #endregion

    #region Functions

    /// <summary>
    /// Serializes the task file.
    /// </summary>
    /// <returns>Indented snake_case JSON.</returns>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JSON_OPTIONS);
    }

    /// <summary>
    /// Deserializes a task file (tests and the fake composer).
    /// </summary>
    /// <param name="json">The document.</param>
    /// <returns>The task.</returns>
    public static ParaViewComposeTask FromJson(string json)
    {
        return JsonSerializer.Deserialize<ParaViewComposeTask>(json, JSON_OPTIONS)
               ?? throw new InvalidOperationException("The compose task document is empty.");
    }

    #endregion

    #region Properties

    /// <summary>Task file contract version.</summary>
    public int Schema { get; set; } = SCHEMA_VERSION;

    /// <summary>Package root the data lives under and the composer runs in.</summary>
    public string PackageRoot { get; set; } = string.Empty;

    /// <summary>Task work directory (scratch files).</summary>
    public string WorkDir { get; set; } = string.Empty;

    /// <summary>Where the composer saves the state (must not exist beforehand).</summary>
    public string StatePath { get; set; } = string.Empty;

    /// <summary>Where the composer writes its status document.</summary>
    public string StatusPath { get; set; } = string.Empty;

    /// <summary>Absolute path of the materialized data file inside the package root.</summary>
    public string DataPath { get; set; } = string.Empty;

    /// <summary>The data file's logical path — what the saved state must reference instead of <see cref="DataPath"/>.</summary>
    public string DataLogicalPath { get; set; } = string.Empty;

    /// <summary>Registration name of the reader proxy (the data file name).</summary>
    public string RegistrationName { get; set; } = string.Empty;

    /// <summary>Path of the bundled reader plugin inside the workspace.</summary>
    public string PluginPath { get; set; } = string.Empty;

    /// <summary>Array to colour by; empty selects the first array of the association.</summary>
    public string ColorArrayName { get; set; } = string.Empty;

    /// <summary>"POINTS" or "CELLS".</summary>
    public string ColorAssociation { get; set; } = ParaViewComposeTokens.POINTS;

    /// <summary>-1 for the magnitude, otherwise the zero-based component.</summary>
    public int ColorComponent { get; set; } = -1;

    /// <summary>Colour-map preset name or empty.</summary>
    public string ColormapPreset { get; set; } = string.Empty;

    /// <summary>ParaView representation type text ("Surface", "Surface With Edges", "Wireframe").</summary>
    public string Representation { get; set; } = ParaViewComposeTokens.SURFACE;

    /// <summary>Whether the scalar bar of the coloured array is shown.</summary>
    public bool ShowScalarBar { get; set; } = true;

    /// <summary>Camera direction token ("isometric", "+x", "-x", "+y", "-y", "+z", "-z").</summary>
    public string CameraDirection { get; set; } = ParaViewComposeTokens.ISOMETRIC;

    /// <summary>Fit token ("all", "last", "first").</summary>
    public string FitTo { get; set; } = ParaViewComposeTokens.FIT_ALL;

    /// <summary>View width the camera is framed for (the output width).</summary>
    public int ViewWidth { get; set; } = 1920;

    /// <summary>View height the camera is framed for (the output height).</summary>
    public int ViewHeight { get; set; } = 1080;

    /// <summary>Most timesteps the "all" fit inspects (evenly sampled, the last always included).</summary>
    public int MaxFitSamples { get; set; } = ParaViewComposeExecutor.MAX_FIT_SAMPLES;

    #endregion
}
