using System.Globalization;
using System.Text.Json;

namespace OutWit.Controller.AI.Smt.Live.Documents;

/// <summary>
/// What a fleet run measured, written by <c>run</c> beside the four documents as
/// <c>fleet.json</c> (<see cref="SCHEMA"/>): the set, and the times from the tool's start ("files
/// ready") to the documents on disk, end to end: connecting, the upload, the job (the server
/// cuts the set into packages, the fleet solves them, the server settles the run and writes the
/// documents), the downloads. The job's own times by the server's clock are kept when the server
/// gives them: the wait in its queue and the run.
/// </summary>
internal sealed class SmtLiveFleetFacts
{
    #region Constants

    /// <summary>The document's schema name.</summary>
    public const string SCHEMA = "smt-live-fleet/1";

    /// <summary>The file it is written to, beside the run's documents.</summary>
    public const string FILE = "fleet.json";

    #endregion

    #region Functions

    /// <summary>
    /// Writes the document.
    /// </summary>
    /// <param name="path">The file; replaced when it is there.</param>
    public void Write(string path)
    {
        using var stream = File.Create(path);
        using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        json.WriteStartObject();
        json.WriteString("schema", SCHEMA);
        json.WriteString("started_utc", Text(StartedUtc));
        json.WriteString("job", Job.ToString("D"));
        json.WriteString("status", Status);

        json.WriteStartObject("problem_set");
        json.WriteString("file", SetFile);
        json.WriteString("sha256", SetSha256);
        json.WriteNumber("bytes", SetBytes);
        json.WriteNumber("rows", Rows);
        json.WriteNumber("taken", Taken);
        json.WriteEndObject();

        json.WriteStartObject("seconds");
        json.WriteNumber("connect", Seconds(Connect));
        json.WriteNumber("upload", Seconds(Upload));
        json.WriteNumber("job", Seconds(JobTime));
        json.WriteNumber("fetch", Seconds(Fetch));
        json.WriteNumber("wall", Seconds(Wall));
        json.WriteEndObject();

        json.WriteNumber("poll_seconds", Seconds(Poll));

        if (SubmittedUtc != null || ServerStartedUtc != null || CompletedUtc != null)
        {
            json.WriteStartObject("server");
            WriteTime(json, "submitted_utc", SubmittedUtc);
            WriteTime(json, "started_utc", ServerStartedUtc);
            WriteTime(json, "completed_utc", CompletedUtc);
            if (SubmittedUtc is { } submitted && ServerStartedUtc is { } started)
                json.WriteNumber("queue_seconds", Seconds(started - submitted));

            if (ServerStartedUtc is { } begun && CompletedUtc is { } ended)
                json.WriteNumber("run_seconds", Seconds(ended - begun));

            json.WriteEndObject();
        }

        json.WriteNumber("problems_per_second", Math.Round(Wall > TimeSpan.Zero ? Taken / Wall.TotalSeconds : 0, 4));
        json.WriteEndObject();
    }

    private static void WriteTime(Utf8JsonWriter json, string name, DateTime? time)
    {
        if (time is { } value)
            json.WriteString(name, Text(value));
        else
            json.WriteNull(name);
    }

    private static string Text(DateTime time)
    {
        return time.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }

    private static double Seconds(TimeSpan time)
    {
        return Math.Round(time.TotalSeconds, 3);
    }

    #endregion

    #region Properties

    /// <summary>When the tool started, UTC.</summary>
    public DateTime StartedUtc { get; init; }

    /// <summary>The job.</summary>
    public Guid Job { get; init; }

    /// <summary>How the job ended.</summary>
    public string Status { get; init; } = "";

    /// <summary>The set's file name (no folder).</summary>
    public string SetFile { get; init; } = "";

    /// <summary>The set's SHA-256.</summary>
    public string SetSha256 { get; init; } = "";

    /// <summary>The set's bytes.</summary>
    public long SetBytes { get; init; }

    /// <summary>The set's rows.</summary>
    public long Rows { get; init; }

    /// <summary>The rows the run took, as its report says.</summary>
    public long Taken { get; init; }

    /// <summary>From the tool's start to the server connected and signed in.</summary>
    public TimeSpan Connect { get; init; }

    /// <summary>Describing and uploading the set.</summary>
    public TimeSpan Upload { get; init; }

    /// <summary>From the submit to the tool's poll that saw the job end (the poll's interval is the error).</summary>
    public TimeSpan JobTime { get; init; }

    /// <summary>Downloading the four documents and holding each to its schema.</summary>
    public TimeSpan Fetch { get; init; }

    /// <summary>From the tool's start to the four documents on disk.</summary>
    public TimeSpan Wall { get; init; }

    /// <summary>How often the tool asked for the job's status.</summary>
    public TimeSpan Poll { get; init; }

    /// <summary>When the server took the job, by its clock.</summary>
    public DateTime? SubmittedUtc { get; init; }

    /// <summary>When the server started it, by its clock.</summary>
    public DateTime? ServerStartedUtc { get; init; }

    /// <summary>When the server ended it, by its clock.</summary>
    public DateTime? CompletedUtc { get; init; }

    #endregion
}
