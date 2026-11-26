using System.Text.Json;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// How the tests read what <see cref="SmtSettleRig.Settle"/> wrote of a run: a row of the results
/// by its index, the report and the integrity document, a number or a count of a map of the
/// integrity document's summary, a fan-out, and the indices of a list of the report.
/// </summary>
internal static class SmtSettledReader
{
    #region Functions

    /// <summary>The row of the results of a problem.</summary>
    /// <param name="run">The documents.</param>
    /// <param name="index">The problem's index.</param>
    /// <returns>The row.</returns>
    public static JsonElement Row(SmtSettledDocuments run, int index)
    {
        return Rows(run).Single(row => row.GetProperty("index").GetInt32() == index);
    }

    /// <summary>Every row of the results, in their order.</summary>
    /// <param name="run">The documents.</param>
    /// <returns>The rows.</returns>
    public static List<JsonElement> Rows(SmtSettledDocuments run)
    {
        return run.Results.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToList();
    }

    /// <summary>The integrity document.</summary>
    /// <param name="run">The documents.</param>
    /// <returns>Its root.</returns>
    public static JsonElement Integrity(SmtSettledDocuments run)
    {
        return JsonDocument.Parse(run.Integrity).RootElement.Clone();
    }

    /// <summary>The report.</summary>
    /// <param name="run">The documents.</param>
    /// <returns>Its root.</returns>
    public static JsonElement Report(SmtSettledDocuments run)
    {
        return JsonDocument.Parse(run.Report).RootElement.Clone();
    }

    /// <summary>A number of the integrity document's summary.</summary>
    /// <param name="run">The documents.</param>
    /// <param name="name">Its name.</param>
    /// <returns>The number.</returns>
    public static int Count(SmtSettledDocuments run, string name)
    {
        return Integrity(run).GetProperty("summary").GetProperty(name).GetInt32();
    }

    /// <summary>A count of a map of the integrity document's summary.</summary>
    /// <param name="run">The documents.</param>
    /// <param name="map">The map.</param>
    /// <param name="key">The count.</param>
    /// <returns>The number; 0 when the key is not there.</returns>
    public static int CountOf(SmtSettledDocuments run, string map, string key)
    {
        return Integrity(run).GetProperty("summary").GetProperty(map).TryGetProperty(key, out var count) ? count.GetInt32() : 0;
    }

    /// <summary>A word of the integrity document's summary.</summary>
    /// <param name="run">The documents.</param>
    /// <param name="name">Its name.</param>
    /// <returns>The word.</returns>
    public static string Word(SmtSettledDocuments run, string name)
    {
        return Integrity(run).GetProperty("summary").GetProperty(name).GetString()!;
    }

    /// <summary>A fan-out of the integrity document.</summary>
    /// <param name="run">The documents.</param>
    /// <param name="round">main, repair or dispute.</param>
    /// <returns>Its record.</returns>
    public static JsonElement FanOut(SmtSettledDocuments run, string round)
    {
        return Integrity(run).GetProperty("fan_outs").EnumerateArray().Single(fanOut => fanOut.GetProperty("round").GetString() == round);
    }

    /// <summary>The indices of a list of the report (an object with <c>indices</c>, or an array).</summary>
    /// <param name="run">The documents.</param>
    /// <param name="path">The names that lead to it from the report's root.</param>
    /// <returns>The indices.</returns>
    public static List<int> Indices(SmtSettledDocuments run, params string[] path)
    {
        var element = Report(run);
        foreach (var name in path)
            element = element.GetProperty(name);

        if (element.ValueKind == JsonValueKind.Object)
            element = element.GetProperty("indices");

        return element.EnumerateArray().Select(index => index.GetInt32()).ToList();
    }

    #endregion
}
