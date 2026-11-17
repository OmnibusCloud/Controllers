using System.Text.Json;
using OutWit.Controller.AI.Verify.Settlement;

namespace OutWit.Controller.AI.Verify.Tests.Settlement;

/// <summary>A settled run as a test reads it: the four documents as text.</summary>
internal sealed record VerifySettledRun(VerifySettlementSummary Summary, string Report, string Outputs, string Provenance, string Integrity, string LedgerPath)
{
    /// <summary>The verdict of every task in the report, by index.</summary>
    public Dictionary<int, string> Verdicts()
    {
        using var document = JsonDocument.Parse(Report);
        return document.RootElement.GetProperty("results").EnumerateArray()
            .ToDictionary(result => result.GetProperty("index").GetInt32(), result => result.GetProperty("verdict").GetString()!);
    }

    /// <summary>A number of the report's summary.</summary>
    public int ReportCount(string name)
    {
        using var document = JsonDocument.Parse(Report);
        return document.RootElement.GetProperty("summary").GetProperty(name).GetInt32();
    }

    /// <summary>A number of the integrity document's summary.</summary>
    public int IntegrityCount(string name)
    {
        using var document = JsonDocument.Parse(Integrity);
        return document.RootElement.GetProperty("summary").GetProperty(name).GetInt32();
    }

    /// <summary>How many result packages the integrity document says were refused for a reason.</summary>
    public int Rejected(string reason)
    {
        using var document = JsonDocument.Parse(Integrity);
        return document.RootElement.GetProperty("summary").GetProperty("rejected").GetProperty(reason).GetInt32();
    }

    /// <summary>The outputs, one parsed line a task, by index.</summary>
    public Dictionary<int, JsonElement> Lines()
    {
        return Outputs.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToDictionary(line => line.GetProperty("index").GetInt32());
    }
}
