using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Writes the integrity document of a run: every task executed more than once, its executions
/// paired, the nodes that ran each, and whether they agree on everything an honest node cannot
/// vary (verdict, exit code, outputs, per-case outcomes). The platform keeps the executions of
/// one batch on different machines; a pair that met on one node all the same proves nothing and
/// is counted, not compared. The mismatch rate is over the compared pairs; a mismatch names the
/// fields that differ so an operator can look.
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
            .SelectMany(batch => batch.Results.Select(result => new Placed(result, batch.Diagnostics)))
            .GroupBy(placed => placed.Result.TaskIndex)
            .OrderBy(task => task.Key)
            .SelectMany(PairsOf)
            .ToList();

        var compared = pairs.Count(pair => pair.Compared);
        var mismatches = pairs.Count(pair => pair.Compared && !pair.Match);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();

            writer.WriteStartObject("summary");
            writer.WriteNumber("sampled", pairs.Count);
            writer.WriteNumber("compared", compared);
            writer.WriteNumber("same_node", pairs.Count(pair => pair.SameNode));
            writer.WriteNumber("unpaired", 0);
            writer.WriteNumber("mismatches", mismatches);
            writer.WriteNumber("mismatch_rate", compared == 0 ? 0.0 : (double)mismatches / compared);
            writer.WriteEndObject();

            writer.WriteStartArray("pairs");
            foreach (var pair in pairs)
                WritePair(writer, pair);
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static IEnumerable<PairRecord> PairsOf(IGrouping<int, Placed> task)
    {
        var ranked = VerifyExecutions.InRuleOrder(task.Select(placed => placed.Result)).ToList();
        var placedByResult = task.ToDictionary(placed => placed.Result, ReferenceEqualityComparer.Instance);
        var primary = placedByResult[ranked[0]];

        return ranked.Skip(1).Select(result => Pair(task.Key, primary, placedByResult[result]));
    }

    private static PairRecord Pair(int index, Placed primary, Placed replica)
    {
        var sameNode = primary.Diagnostics != null
                       && replica.Diagnostics != null
                       && primary.Diagnostics.NodeId == replica.Diagnostics.NodeId;

        var differing = Differences(primary.Result, replica.Result);
        return new PairRecord(index, primary, replica, sameNode, differing.Count == 0, differing);
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
        writer.WriteBoolean("compared", pair.Compared);
        if (pair.Compared)
        {
            writer.WriteBoolean("match", pair.Match);
            if (!pair.Match)
            {
                writer.WriteStartArray("differs");
                foreach (var field in pair.Differing)
                    writer.WriteStringValue(field);
                writer.WriteEndArray();
            }
        }

        writer.WriteEndObject();
    }

    private static void WriteSide(Utf8JsonWriter writer, string name, Placed placed)
    {
        writer.WriteStartObject(name);
        if (placed.Diagnostics != null)
        {
            writer.WriteString("node_id", placed.Diagnostics.NodeId);
            writer.WriteString("node_name", placed.Diagnostics.NodeName);
        }

        writer.WriteString("verdict", placed.Result.Verdict.ToString());
        writer.WriteNumber("exit", placed.Result.ExitCode);
        writer.WriteEndObject();
    }

    #endregion

    #region Types

    private sealed record Placed(VerifyResultData Result, VerifyBatchDiagnosticsData? Diagnostics);

    /// <summary>Two executions of one task: compared only when they ran on different nodes.</summary>
    private sealed record PairRecord(int Index, Placed Primary, Placed Replica, bool SameNode, bool Match, List<string> Differing)
    {
        /// <summary>Whether the pair was compared: it ran on different nodes.</summary>
        public bool Compared => !SameNode;
    }

    #endregion
}
