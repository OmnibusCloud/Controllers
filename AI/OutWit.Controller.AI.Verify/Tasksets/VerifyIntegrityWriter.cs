using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Writes the integrity document of a run: every sampled task paired with its replica, the
/// nodes that ran each, and whether the two executions agree on everything an honest node
/// cannot vary (verdict, exit code, outputs, per-case outcomes). A pair that landed on one node
/// proves nothing about that node and is counted, not compared. The mismatch rate is over the
/// compared pairs; a mismatch names the fields that differ so an operator can look.
/// </summary>
public static class VerifyIntegrityWriter
{
    #region Functions

    /// <summary>
    /// Serializes the pairs as UTF-8 JSON.
    /// </summary>
    /// <param name="primaries">The primary result batches.</param>
    /// <param name="replicas">The replica result batches (the sampled re-execution).</param>
    /// <returns>The JSON document bytes.</returns>
    public static byte[] Write(IReadOnlyList<VerifyResultBatchData> primaries, IReadOnlyList<VerifyResultBatchData> replicas)
    {
        var primaryByIndex = Index(primaries);
        var pairs = Index(replicas)
            .OrderBy(entry => entry.Key)
            .Select(entry => Pair(entry.Key, primaryByIndex.GetValueOrDefault(entry.Key), entry.Value))
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
            writer.WriteNumber("unpaired", pairs.Count(pair => pair.Primary == null));
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

    private static Dictionary<int, Placed> Index(IReadOnlyList<VerifyResultBatchData> batches)
    {
        var byIndex = new Dictionary<int, Placed>();
        foreach (var batch in batches)
        {
            foreach (var result in batch.Results)
                byIndex.TryAdd(result.TaskIndex, new Placed(result, batch.Diagnostics));
        }

        return byIndex;
    }

    private static PairRecord Pair(int index, Placed? primary, Placed replica)
    {
        if (primary == null)
            return new PairRecord(index, null, replica, false, false, []);

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

    private static void WriteSide(Utf8JsonWriter writer, string name, Placed? placed)
    {
        if (placed == null)
        {
            writer.WriteNull(name);
            return;
        }

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

    /// <summary>A sampled task: compared only when both sides exist and ran on different nodes.</summary>
    private sealed record PairRecord(int Index, Placed? Primary, Placed Replica, bool SameNode, bool Match, List<string> Differing)
    {
        public bool Compared => Primary != null && !SameNode;
    }

    #endregion
}
