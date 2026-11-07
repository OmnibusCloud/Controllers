using System.Text.Json;
using System.Text.Json.Serialization;

namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// The runner contract, adapter → runner: one JSON task file carrying everything the controller-owned
/// pvpython runner needs (docs 03, section 9 — the same inputs as the documented argument list,
/// carried as one bounded document so no value is ever shell-parsed), including the effective proxy
/// allowlist and the blocked proxy/property lists for the post-load runtime check. Schema 2 (controller
/// 0.5.0, FrameBatch): the state, view, size, format and policy lists are shared once, and the outputs
/// the process renders — one for ParaView.RenderFrame, a chunk for ParaView.RenderFrameBatch — travel
/// as <see cref="Outputs"/>, each with its timestep, camera move and file.
/// </summary>
public sealed class ParaViewRunnerTask
{
    #region Constants

    /// <summary>Version of the task file contract.</summary>
    public const int SCHEMA_VERSION = 2;

    /// <summary>File name of the task file inside the work directory.</summary>
    public const string FILE_NAME = "task.json";

    /// <summary>Runner argument introducing the task file.</summary>
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
    /// Deserializes a task file (tests and the fake runner).
    /// </summary>
    /// <param name="json">The document.</param>
    /// <returns>The task.</returns>
    public static ParaViewRunnerTask FromJson(string json)
    {
        return JsonSerializer.Deserialize<ParaViewRunnerTask>(json, JSON_OPTIONS)
               ?? throw new InvalidOperationException("The runner task document is empty.");
    }

    #endregion

    #region Properties

    /// <summary>Task file contract version.</summary>
    public int Schema { get; set; } = SCHEMA_VERSION;

    /// <summary>Identity of the task or batch (diagnostics only; the first output's task identity).</summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>Absolute path of the materialized state inside the package root.</summary>
    public string StatePath { get; set; } = string.Empty;

    /// <summary>Absolute package root every logical path resolves under.</summary>
    public string PackageRoot { get; set; } = string.Empty;

    /// <summary>Absolute task work directory (outputs and status must stay inside it).</summary>
    public string WorkDir { get; set; } = string.Empty;

    /// <summary>Absolute path the runner writes its status document to.</summary>
    public string StatusPath { get; set; } = string.Empty;

    /// <summary>Registration name of the view to render.</summary>
    public string ViewId { get; set; } = string.Empty;

    /// <summary>Output width.</summary>
    public int Width { get; set; }

    /// <summary>Output height.</summary>
    public int Height { get; set; }

    /// <summary>Output format: png or jpeg.</summary>
    public string Format { get; set; } = "png";

    /// <summary>Render with a transparent background.</summary>
    public bool TransparentBackground { get; set; }

    /// <summary>Absolute path of the bundled reader to load, null when the package requires none.</summary>
    public string? PluginPath { get; set; }

    /// <summary>Effective allowlisted group/type keys.</summary>
    public List<string> AllowedProxies { get; set; } = [];

    /// <summary>Proxy types that must not exist after load.</summary>
    public List<string> BlockedProxyTypes { get; set; } = [];

    /// <summary>Property names that must not carry code after load.</summary>
    public List<string> BlockedPropertyNames { get; set; } = [];

    /// <summary>Property names that always denote file references.</summary>
    public List<string> FilePropertyNames { get; set; } = [];

    /// <summary>XML groups whose proxies read files.</summary>
    public List<string> FileReferenceGroups { get; set; } = [];

    /// <summary>Maximum byte size the runner accepts for the state (mirrors the host limit).</summary>
    public long MaxStateBytes { get; set; }

    /// <summary>Maximum logical path length (mirrors the host limit).</summary>
    public int MaxLogicalPathChars { get; set; }

    /// <summary>
    /// The outputs to render in order, each with its timestep, camera move and file — one for a
    /// single-frame task, the chunk for a batch. The state loads once; every output selects,
    /// moves the camera from the state's captured framing, renders and verifies.
    /// </summary>
    public List<ParaViewRunnerOutput> Outputs { get; set; } = [];

    #endregion
}
