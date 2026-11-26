using System.Text.Json;
using OutWit.Controller.AI.Smt.Live.Documents;
using OutWit.Controller.AI.Smt.Live.Local;

namespace OutWit.Controller.AI.Smt.Live.Comparative;

/// <summary>
/// What one side of the comparative test measured, as its folder's <c>local.json</c> or
/// <c>fleet.json</c> says: the problems it took, its wall time from the tool's start to the
/// documents on disk, its parts in order, and for a local run its fixed part (the start-up to the
/// sandbox ready). Numbers are read as written; none is made up.
/// </summary>
internal sealed class SmtLiveMeasured
{
    #region Constructors

    private SmtLiveMeasured(long taken, double wall, double? startup, IReadOnlyList<(string Name, double Seconds)> parts, string description)
    {
        Taken = taken;
        Wall = wall;
        Startup = startup;
        Parts = parts;
        Description = description;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Reads a local run's <c>local.json</c>.
    /// </summary>
    /// <param name="folder">The folder <c>local</c> wrote.</param>
    /// <returns>The measurement.</returns>
    /// <exception cref="InvalidDataException">The document is not a local run's.</exception>
    public static SmtLiveMeasured ReadLocal(string folder)
    {
        using var document = Parse(Path.Combine(folder, SmtLiveLocalFacts.FILE), SmtLiveLocalFacts.SCHEMA);
        var root = document.RootElement;
        var seconds = Object(root, "seconds");
        var machine = Object(root, "machine");
        var degree = Object(root, "degree");

        var parts = new List<(string, double)>
        {
            ("start-up", Number(seconds, "startup")),
            ("read", Number(seconds, "read")),
            ("solve", Number(seconds, "solve")),
            ("settle", Number(seconds, "settle"))
        };

        var description = $"1 machine: {Text(machine, "platform")}, {Integer(machine, "cores")} cores, {Integer(machine, "memory_bytes") / (1024.0 * 1024 * 1024):0.0} GiB; " +
                          $"{Integer(degree, "sessions")} session(s) at once ({Text(degree, "decided_by")})";
        return new SmtLiveMeasured(Integer(Object(root, "problem_set"), "taken"), Number(seconds, "wall"), Number(seconds, "startup"), parts, description);
    }

    /// <summary>
    /// Reads a fleet run's <c>fleet.json</c>.
    /// </summary>
    /// <param name="folder">The folder <c>run</c> wrote.</param>
    /// <returns>The measurement.</returns>
    /// <exception cref="InvalidDataException">The document is not a fleet run's.</exception>
    public static SmtLiveMeasured ReadFleet(string folder)
    {
        using var document = Parse(Path.Combine(folder, SmtLiveFleetFacts.FILE), SmtLiveFleetFacts.SCHEMA);
        var root = document.RootElement;
        var seconds = Object(root, "seconds");

        var parts = new List<(string, double)>
        {
            ("connect", Number(seconds, "connect")),
            ("upload", Number(seconds, "upload")),
            ("job", Number(seconds, "job")),
            ("fetch", Number(seconds, "fetch"))
        };

        var description = "the fleet's machines (not named)";
        if (root.TryGetProperty("server", out var server) && server.ValueKind == JsonValueKind.Object)
        {
            if (server.TryGetProperty("queue_seconds", out var queue) && queue.TryGetDouble(out var waited))
                parts.Add(("of the job: queue", waited));

            if (server.TryGetProperty("run_seconds", out var run) && run.TryGetDouble(out var ran))
                parts.Add(("of the job: run", ran));
        }

        return new SmtLiveMeasured(Integer(Object(root, "problem_set"), "taken"), Number(seconds, "wall"), null, parts, description);
    }

    /// <summary>Whether a folder holds a measurement of the side asked for.</summary>
    /// <param name="folder">The folder.</param>
    /// <param name="local">True for <c>local.json</c>, false for <c>fleet.json</c>.</param>
    /// <returns>True when the file is there.</returns>
    public static bool IsIn(string folder, bool local)
    {
        return File.Exists(Path.Combine(folder, local ? SmtLiveLocalFacts.FILE : SmtLiveFleetFacts.FILE));
    }

    private static JsonDocument Parse(string path, string schema)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllBytes(path));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} is not JSON: {exception.Message}");
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("schema", out var name) || name.GetString() != schema)
        {
            document.Dispose();
            throw new InvalidDataException($"{Path.GetFileName(path)} is not a {schema} document");
        }

        return document;
    }

    private static JsonElement Object(JsonElement parent, string name)
    {
        return parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw new InvalidDataException($"the measurement has no object '{name}'");
    }

    private static double Number(JsonElement parent, string name)
    {
        return parent.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) && double.IsFinite(number) && number >= 0
            ? number
            : throw new InvalidDataException($"the measurement has no number '{name}'");
    }

    private static long Integer(JsonElement parent, string name)
    {
        return parent.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) && number >= 0
            ? number
            : throw new InvalidDataException($"the measurement has no whole number '{name}'");
    }

    private static string Text(JsonElement parent, string name)
    {
        return parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
    }

    #endregion

    #region Properties

    /// <summary>The problems the run took.</summary>
    public long Taken { get; }

    /// <summary>Seconds from the tool's start to the documents on disk.</summary>
    public double Wall { get; }

    /// <summary>A local run's start-up to the sandbox ready, seconds; null for a fleet run.</summary>
    public double? Startup { get; }

    /// <summary>The parts of the wall time, in order, seconds.</summary>
    public IReadOnlyList<(string Name, double Seconds)> Parts { get; }

    /// <summary>The machines, in short.</summary>
    public string Description { get; }

    /// <summary>The problems a second, end to end; 0 for a run of no time.</summary>
    public double Rate => Wall > 0 ? Taken / Wall : 0;

    #endregion
}
