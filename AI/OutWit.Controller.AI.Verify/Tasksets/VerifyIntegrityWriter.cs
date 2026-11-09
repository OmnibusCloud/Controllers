using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Writes the integrity document of a run: every task executed more than once, its executions
/// paired, and whether they agree on everything an honest node cannot vary (verdict, exit code,
/// outputs, per-case outcomes). The platform keeps the executions of one batch on different
/// machines, and the document does not say which. The mismatch rate is over the pairs; a
/// mismatch names the fields that differ so an operator can look.
/// </summary>
public static class VerifyIntegrityWriter
{
    #region Functions

    /// <summary>
    /// Serializes the pairs as UTF-8 JSON.
    /// </summary>
    /// <param name="executions">The result batches of all executions, any order; a task executed once makes no pair.</param>
    /// <returns>The JSON document bytes.</returns>
    public static byte[] Write(IReadOnlyList<VerifyResultBatchData> executions)
    {
        var pairs = executions
            .SelectMany(batch => batch.Results)
            .GroupBy(result => result.TaskIndex)
            .OrderBy(task => task.Key)
            .SelectMany(PairsOf)
            .ToList();

        var mismatches = pairs.Count(pair => !pair.Match);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();

            writer.WriteStartObject("summary");
            writer.WriteNumber("sampled", pairs.Count);
            writer.WriteNumber("compared", pairs.Count);
            writer.WriteNumber("mismatches", mismatches);
            writer.WriteNumber("mismatch_rate", pairs.Count == 0 ? 0.0 : (double)mismatches / pairs.Count);
            writer.WriteEndObject();

            writer.WriteStartArray("pairs");
            foreach (var pair in pairs)
                WritePair(writer, pair);
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static IEnumerable<PairRecord> PairsOf(IGrouping<int, VerifyResultData> task)
    {
        var ranked = VerifyExecutions.InRuleOrder(task).ToList();

        return ranked.Skip(1).Select(replica => Pair(task.Key, ranked[0], replica));
    }

    private static PairRecord Pair(int index, VerifyResultData primary, VerifyResultData replica)
    {
        var differing = Differences(primary, replica);
        return new PairRecord(index, primary, replica, differing.Count == 0, differing);
    }

    private static List<string> Differences(VerifyResultData primary, VerifyResultData replica)
    {
        var fields = new List<string>();
        if (primary.Verdict != replica.Verdict)
            fields.Add("verdict");
        if (primary.ExitCode != replica.ExitCode)
            fields.Add("exit");
        if (primary.Stdout != replica.Stdout || primary.StdoutTruncated != replica.StdoutTruncated)
            fields.Add("stdout");
        if (primary.Stderr != replica.Stderr || primary.StderrTruncated != replica.StderrTruncated)
            fields.Add("stderr");
        if (primary.CaseResults.Count != replica.CaseResults.Count
            || primary.CaseResults.Where((c, i) => !c.Is(replica.CaseResults[i])).Any())
            fields.Add("cases");

        return fields;
    }

    private static void WritePair(Utf8JsonWriter writer, PairRecord pair)
    {
        writer.WriteStartObject();
        writer.WriteNumber("index", pair.Index);
        WriteSide(writer, "primary", pair.Primary);
        WriteSide(writer, "replica", pair.Replica);
        writer.WriteBoolean("compared", true);
        writer.WriteBoolean("match", pair.Match);
        if (!pair.Match)
        {
            writer.WriteStartArray("differs");
            foreach (var field in pair.Differing)
                writer.WriteStringValue(field);
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static void WriteSide(Utf8JsonWriter writer, string name, VerifyResultData result)
    {
        writer.WriteStartObject(name);
        writer.WriteString("verdict", result.Verdict.ToString());
        writer.WriteNumber("exit", result.ExitCode);
        writer.WriteEndObject();
    }

    #endregion

    #region Types

    /// <summary>Two executions of one task and the fields they disagree on.</summary>
    private sealed record PairRecord(int Index, VerifyResultData Primary, VerifyResultData Replica, bool Match, List<string> Differing);

    #endregion
}
