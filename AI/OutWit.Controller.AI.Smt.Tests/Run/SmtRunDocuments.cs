using System.Text.Json;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Run;

/// <summary>The four documents of one run of SmtRun, read from the blobs the job's variables name, and their texts.</summary>
internal sealed class SmtRunDocuments
{
    #region Constructors

    private SmtRunDocuments(string results, string report, string integrity, string provenance, IWitJob job)
    {
        ResultsText = results;
        ReportText = report;
        IntegrityText = integrity;
        ProvenanceText = provenance;
        Job = job;

        Rows = results.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToList();
        Report = JsonDocument.Parse(report).RootElement.Clone();
        Integrity = JsonDocument.Parse(integrity).RootElement.Clone();
        Provenance = JsonDocument.Parse(provenance).RootElement.Clone();
    }

    #endregion

    #region Functions

    /// <summary>Reads the documents a finished job names.</summary>
    /// <param name="job">The job.</param>
    /// <param name="blobs">The blob store.</param>
    /// <returns>The documents.</returns>
    public static async Task<SmtRunDocuments> ReadAsync(IWitJob job, SmtTestBlobService blobs)
    {
        async Task<string> Text(string name)
        {
            return await File.ReadAllTextAsync(await blobs.GetLocalPathAsync((Guid)job.Variables[name].Value!));
        }

        return new SmtRunDocuments(await Text("results"), await Text("report"), await Text("integrity"), await Text("provenance"), job);
    }

    /// <summary>The row of a problem.</summary>
    /// <param name="index">The problem's index.</param>
    /// <returns>The row.</returns>
    public JsonElement Row(int index)
    {
        return Rows.Single(row => row.GetProperty("index").GetInt32() == index);
    }

    /// <summary>A number of the integrity document's summary.</summary>
    /// <param name="name">Its name.</param>
    /// <returns>The number.</returns>
    public int IntegrityCount(string name)
    {
        return Integrity.GetProperty("summary").GetProperty(name).GetInt32();
    }

    /// <summary>A count of an object of the integrity document's summary.</summary>
    /// <param name="name">The object.</param>
    /// <param name="key">The count.</param>
    /// <returns>The number; 0 when it is not there.</returns>
    public int IntegrityCount(string name, string key)
    {
        return Integrity.GetProperty("summary").GetProperty(name).TryGetProperty(key, out var count) ? count.GetInt32() : 0;
    }

    /// <summary>A fan-out of the integrity document.</summary>
    /// <param name="round">main, repair or dispute.</param>
    /// <returns>Its packages, copies, selected (-1 for null), complete and unanswered.</returns>
    public (int Packages, int Copies, int Selected, int Complete, int Unanswered) FanOut(string round)
    {
        var fanOut = Integrity.GetProperty("fan_outs").EnumerateArray().Single(item => item.GetProperty("round").GetString() == round);
        var selected = fanOut.GetProperty("selected");

        return (fanOut.GetProperty("packages").GetInt32(), fanOut.GetProperty("copies").GetInt32(), selected.ValueKind == JsonValueKind.Null ? -1 : selected.GetInt32(),
            fanOut.GetProperty("complete").GetInt32(), fanOut.GetProperty("unanswered").GetInt32());
    }

    /// <summary>A count of an object of the report.</summary>
    /// <param name="name">The object.</param>
    /// <param name="key">The count.</param>
    /// <returns>The number; 0 when it is not there.</returns>
    public int ReportCount(string name, string key)
    {
        return Report.GetProperty(name).TryGetProperty(key, out var count) ? count.GetInt32() : 0;
    }

    #endregion

    #region Properties

    /// <summary>The rows of the results, in their order.</summary>
    public List<JsonElement> Rows { get; }

    /// <summary>The report.</summary>
    public JsonElement Report { get; }

    /// <summary>The integrity document.</summary>
    public JsonElement Integrity { get; }

    /// <summary>The provenance.</summary>
    public JsonElement Provenance { get; }

    /// <summary>The results as they were stored.</summary>
    public string ResultsText { get; }

    /// <summary>The report as it was stored.</summary>
    public string ReportText { get; }

    /// <summary>The integrity document as it was stored.</summary>
    public string IntegrityText { get; }

    /// <summary>The provenance as it was stored.</summary>
    public string ProvenanceText { get; }

    /// <summary>Every document together, for what none of them may say.</summary>
    public string AllText => ResultsText + ReportText + IntegrityText + ProvenanceText;

    /// <summary>The job: its variables.</summary>
    public IWitJob Job { get; }

    #endregion
}
