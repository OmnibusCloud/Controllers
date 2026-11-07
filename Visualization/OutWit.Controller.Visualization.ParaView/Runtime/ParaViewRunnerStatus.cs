using System.Text.Json;
using System.Text.Json.Serialization;

namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// The runner contract, runner → adapter: the bounded machine-readable status document the runner
/// writes on every exit path (docs 03, section 9.7). Absent or malformed status with a zero exit
/// code is treated as a failure — the adapter never infers success from the exit code alone. Schema 2
/// (controller 0.5.0, FrameBatch) adds the per-output verdicts in <see cref="Outputs"/>; the
/// task-level <see cref="Ok"/> holds only when every output rendered and verified.
/// </summary>
public sealed class ParaViewRunnerStatus
{
    #region Constants

    /// <summary>Version of the status document contract.</summary>
    public const int SCHEMA_VERSION = 2;

    /// <summary>File name of the status document inside the work directory.</summary>
    public const string FILE_NAME = "status.json";

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
    public static ParaViewRunnerStatus? TryRead(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0 || info.Length > MAX_STATUS_BYTES)
                return null;

            return JsonSerializer.Deserialize<ParaViewRunnerStatus>(File.ReadAllText(path), JSON_OPTIONS);
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
    /// Serializes a status document (the fake runner and tests).
    /// </summary>
    /// <returns>snake_case JSON.</returns>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JSON_OPTIONS);
    }

    /// <summary>
    /// The first output the runner reported as failed, or null when every reported output is ok.
    /// </summary>
    /// <returns>The failed output status or null.</returns>
    public ParaViewRunnerOutputStatus? FirstFailedOutput()
    {
        return Outputs.FirstOrDefault(me => !me.Ok);
    }

    #endregion

    #region Properties

    /// <summary>Status document contract version.</summary>
    public int Schema { get; set; }

    /// <summary>True when the runner rendered and verified every output.</summary>
    public bool Ok { get; set; }

    /// <summary>Runner stage reached (load-state, validate, select, orbit, render, verify-output, done).</summary>
    public string Stage { get; set; } = string.Empty;

    /// <summary>Bounded error text when not ok.</summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>ParaView version as reported by the runtime.</summary>
    public string ParaviewVersion { get; set; } = string.Empty;

    /// <summary>Loaded reader version, empty when no plugin was loaded.</summary>
    public string ReaderVersion { get; set; } = string.Empty;

    /// <summary>Number of proxies instantiated after load.</summary>
    public int ProxyCount { get; set; }

    /// <summary>Seconds spent in the render calls, summed over the outputs.</summary>
    public double RenderSeconds { get; set; }

    /// <summary>Output width the runner rendered.</summary>
    public int Width { get; set; }

    /// <summary>Output height the runner rendered.</summary>
    public int Height { get; set; }

    /// <summary>Selected render window backend, when the runtime reports it.</summary>
    public string Backend { get; set; } = string.Empty;

    /// <summary>VTK error events observed during load and render (any makes the task fail).</summary>
    public int VtkErrors { get; set; }

    /// <summary>File-series references the task did not materialize (informational).</summary>
    public int MissingReferences { get; set; }

    /// <summary>Per-output verdicts in output order (schema 2); an output the runner never reached is absent.</summary>
    public List<ParaViewRunnerOutputStatus> Outputs { get; set; } = [];

    #endregion
}
