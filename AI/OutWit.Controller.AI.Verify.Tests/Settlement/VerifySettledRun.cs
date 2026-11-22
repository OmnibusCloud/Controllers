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

    /// <summary>A word of the report's summary.</summary>
    public string? ReportWord(string name)
    {
        using var document = JsonDocument.Parse(Report);
        return document.RootElement.GetProperty("summary").GetProperty(name).GetString();
    }

    /// <summary>A word of the integrity document's summary.</summary>
    public string? IntegrityWord(string name)
    {
        using var document = JsonDocument.Parse(Integrity);
        return document.RootElement.GetProperty("summary").GetProperty(name).GetString();
    }

    /// <summary>The sampling rate the report states, or null when it states none.</summary>
    public double? ReportRate()
    {
        using var document = JsonDocument.Parse(Report);
        var rate = document.RootElement.GetProperty("summary").GetProperty("sampling_rate");
        return rate.ValueKind == JsonValueKind.Null ? null : rate.GetDouble();
    }

    /// <summary>A fan-out of the integrity document as its numbers: packages, copies, selected (-1 for null), complete, unanswered.</summary>
    public (int Packages, int Copies, int Selected, int Complete, int Unanswered) FanOut(string round)
    {
        using var document = JsonDocument.Parse(Integrity);
        var fanOut = document.RootElement.GetProperty("fan_outs").EnumerateArray().Single(item => item.GetProperty("round").GetString() == round);
        var selected = fanOut.GetProperty("selected");

        return (fanOut.GetProperty("packages").GetInt32(), fanOut.GetProperty("copies").GetInt32(), selected.ValueKind == JsonValueKind.Null ? -1 : selected.GetInt32(),
            fanOut.GetProperty("complete").GetInt32(), fanOut.GetProperty("unanswered").GetInt32());
    }

    /// <summary>The rounds of the pairs of the integrity document, by task.</summary>
    public Dictionary<int, string> PairRounds()
    {
        using var document = JsonDocument.Parse(Integrity);
        return document.RootElement.GetProperty("pairs").EnumerateArray()
            .GroupBy(pair => pair.GetProperty("index").GetInt32())
            .ToDictionary(pairs => pairs.Key, pairs => pairs.First().GetProperty("round").GetString()!);
    }

    /// <summary>How every dispute of the integrity document ended, by task.</summary>
    public Dictionary<int, string> DisputeOutcomes()
    {
        using var document = JsonDocument.Parse(Integrity);
        return document.RootElement.GetProperty("disputes").EnumerateArray()
            .ToDictionary(dispute => dispute.GetProperty("index").GetInt32(), dispute => dispute.GetProperty("outcome").GetString()!);
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
