using System.Globalization;
using System.Text.Json;

namespace OutWit.Controller.AI.Smt.Live.Local;

/// <summary>
/// What a local run measured and ran with, written beside its four documents as
/// <c>local.json</c> (<see cref="SCHEMA"/>): the machine without a name, the degree and what
/// decided it, the module's and the native engine library's SHA-256 as loaded, the set, and the
/// times from the tool's start ("files ready") to the documents on disk.
/// </summary>
internal sealed class SmtLiveLocalFacts
{
    #region Constants

    /// <summary>The document's schema name.</summary>
    public const string SCHEMA = "smt-live-local/1";

    /// <summary>The file it is written to, beside the run's documents.</summary>
    public const string FILE = "local.json";

    #endregion

    #region Functions

    /// <summary>
    /// Writes the document.
    /// </summary>
    /// <param name="path">The file; replaced when it is there.</param>
    public void Write(string path)
    {
        using var stream = File.Create(path);
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("schema", SCHEMA);
            json.WriteString("started_utc", StartedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

            json.WriteStartObject("machine");
            json.WriteString("os", Machine.Os);
            json.WriteString("os_version", Machine.OsVersion);
            json.WriteString("platform", Machine.Platform);
            json.WriteNumber("cores", Machine.Cores);
            json.WriteNumber("memory_bytes", Machine.MemoryBytes);
            json.WriteNumber("available_bytes", Machine.AvailableBytes);
            json.WriteEndObject();

            json.WriteStartObject("degree");
            json.WriteNumber("sessions", Degree);
            json.WriteString("decided_by", DecidedBy);
            json.WriteNumber("rule_sessions", RuleDegree);
            json.WriteString("rule_decided_by", RuleDecidedBy);
            json.WriteNumber("session_bytes", SessionBytes);
            json.WriteEndObject();

            json.WriteStartObject("solver");
            json.WriteString("profile", Profile);
            json.WriteString("controller", Controller);
            json.WriteString("module", Module);
            json.WriteString("module_sha256", ModuleSha256);
            json.WriteBoolean("module_pinned", ModulePinned);
            json.WriteString("engine", Engine);
            if (NativesSha256 == null)
                json.WriteNull("natives_sha256");
            else
                json.WriteString("natives_sha256", NativesSha256);
            json.WriteBoolean("natives_pinned", NativesPinned);
            json.WriteBoolean("explicit_traps", ExplicitTraps);
            json.WriteEndObject();

            json.WriteStartObject("problem_set");
            json.WriteString("file", SetFile);
            json.WriteString("sha256", SetSha256);
            json.WriteNumber("bytes", SetBytes);
            json.WriteNumber("rows", Rows);
            json.WriteNumber("taken", Taken);
            json.WriteNumber("refused", Refused);
            json.WriteEndObject();

            json.WriteNumber("packages", Packages);

            json.WriteStartObject("seconds");
            json.WriteNumber("startup", Seconds(Startup));
            json.WriteNumber("read", Seconds(Read));
            json.WriteNumber("solve", Seconds(Solve));
            json.WriteNumber("settle", Seconds(Settle));
            json.WriteNumber("wall", Seconds(Wall));
            json.WriteEndObject();

            json.WriteNumber("problems_per_second", Math.Round(Wall > TimeSpan.Zero ? Taken / Wall.TotalSeconds : 0, 4));
            json.WriteEndObject();
        }
    }

    private static double Seconds(TimeSpan time)
    {
        return Math.Round(time.TotalSeconds, 3);
    }

    #endregion

    #region Properties

    /// <summary>When the tool started, UTC.</summary>
    public DateTime StartedUtc { get; init; }

    /// <summary>The machine.</summary>
    public required SmtLiveMachine Machine { get; init; }

    /// <summary>The sessions at once the run used.</summary>
    public int Degree { get; init; }

    /// <summary>What decided them: the node's rule's reason (cores, cap, memory), or <c>given</c>.</summary>
    public string DecidedBy { get; init; } = "";

    /// <summary>The sessions at once the node's rule gives on this machine.</summary>
    public int RuleDegree { get; init; }

    /// <summary>What decided the rule's degree.</summary>
    public string RuleDecidedBy { get; init; } = "";

    /// <summary>The memory one session may take under the run's options.</summary>
    public long SessionBytes { get; init; }

    /// <summary>The solver profile.</summary>
    public string Profile { get; init; } = "";

    /// <summary>The controller's version.</summary>
    public string Controller { get; init; } = "";

    /// <summary>The module, relative to the controller's folder, or <c>WITAI_SMT_Z3</c> when the variable named it.</summary>
    public string Module { get; init; } = "";

    /// <summary>The module's SHA-256 as loaded.</summary>
    public string ModuleSha256 { get; init; } = "";

    /// <summary>Whether it is the profile's pin.</summary>
    public bool ModulePinned { get; init; }

    /// <summary>The engine and its version.</summary>
    public string Engine { get; init; } = "";

    /// <summary>The native library's SHA-256 as loaded; null when the process cannot tell.</summary>
    public string? NativesSha256 { get; init; }

    /// <summary>Whether it is the pin of this platform.</summary>
    public bool NativesPinned { get; init; }

    /// <summary>Whether the sandbox makes every trap an explicit check.</summary>
    public bool ExplicitTraps { get; init; }

    /// <summary>The set's file name (no folder).</summary>
    public string SetFile { get; init; } = "";

    /// <summary>The set's SHA-256.</summary>
    public string SetSha256 { get; init; } = "";

    /// <summary>The set's bytes.</summary>
    public long SetBytes { get; init; }

    /// <summary>The set's rows.</summary>
    public long Rows { get; init; }

    /// <summary>The rows taken.</summary>
    public long Taken { get; init; }

    /// <summary>The rows refused.</summary>
    public long Refused { get; init; }

    /// <summary>The packages the set was cut into.</summary>
    public int Packages { get; init; }

    /// <summary>From the tool's start to the sandbox ready: the runtime's start and the module's compile.</summary>
    public TimeSpan Startup { get; init; }

    /// <summary>Reading the set and cutting it into packages.</summary>
    public TimeSpan Read { get; init; }

    /// <summary>The pool: the first problem handed out to the last row written.</summary>
    public TimeSpan Solve { get; init; }

    /// <summary>Settling the run (the evaluator on every sat model) and writing the documents.</summary>
    public TimeSpan Settle { get; init; }

    /// <summary>From the tool's start to the four documents on disk.</summary>
    public TimeSpan Wall { get; init; }

    #endregion
}
