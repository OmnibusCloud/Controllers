using System.Text.Json;
using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Reports;

/// <summary>
/// Writes the integrity document of a run: every sampled shard paired with its replica, the
/// nodes that ran each, and whether the two executions agree on everything an honest node
/// cannot vary — the verdict, the content hash of the product, the document count, the drop
/// and language histograms. A pair that landed on one node proves nothing about that node and
/// is counted, not compared. Schema <c>corpus-integrity/1</c>.
/// </summary>
public static class CorpusIntegrityWriter
{
    #region Constants

    /// <summary>The document schema name.</summary>
    public const string SCHEMA = "corpus-integrity/1";

    #endregion

    #region Functions

    /// <summary>Serializes the pairs as UTF-8 JSON.</summary>
    /// <param name="primaries">The primary results.</param>
    /// <param name="replicas">The replica results (the sampled re-execution).</param>
    /// <returns>The JSON document bytes.</returns>
    public static byte[] Write(IReadOnlyList<CorpusResultData> primaries, IReadOnlyList<CorpusResultData> replicas)
    {
        var primaryByIndex = new Dictionary<int, CorpusResultData>();
        foreach (var primary in primaries)
            primaryByIndex.TryAdd(primary.TaskIndex, primary);

        var replicaByIndex = new Dictionary<int, CorpusResultData>();
        foreach (var replica in replicas)
            replicaByIndex.TryAdd(replica.TaskIndex, replica);

        var pairs = replicaByIndex
            .OrderBy(entry => entry.Key)
            .Select(entry => Pair(entry.Key, primaryByIndex.GetValueOrDefault(entry.Key), entry.Value))
            .ToList();

        var compared = pairs.Count(pair => pair.Compared);
        var mismatches = pairs.Count(pair => pair.Compared && !pair.Match);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", SCHEMA);

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

    /// <summary>The fields two results of one shard disagree on; empty when they agree.</summary>
    /// <param name="primary">The primary result.</param>
    /// <param name="replica">The replica result.</param>
    /// <returns>Field names: verdict, content, documents, drops, languages.</returns>
    public static List<string> Differences(CorpusResultData primary, CorpusResultData replica)
    {
        var fields = new List<string>();
        if (primary.Verdict != replica.Verdict)
            fields.Add("verdict");
        if (!string.Equals(primary.ContentSha256, replica.ContentSha256, StringComparison.Ordinal))
            fields.Add("content");

        var primaryStats = primary.Stats ?? new CorpusShardStatsData();
        var replicaStats = replica.Stats ?? new CorpusShardStatsData();
        if (primaryStats.DocumentsOut != replicaStats.DocumentsOut || primaryStats.DocumentsExtracted != replicaStats.DocumentsExtracted || primaryStats.RecordsIn != replicaStats.RecordsIn)
            fields.Add("documents");
        if (!SameCounts(primaryStats.Drops, replicaStats.Drops))
            fields.Add("drops");
        if (!SameCounts(primaryStats.Languages, replicaStats.Languages))
            fields.Add("languages");

        return fields;
    }

    private static bool SameCounts(IReadOnlyList<CorpusCountData> first, IReadOnlyList<CorpusCountData> second)
    {
        return first.Count == second.Count && first.Zip(second).All(pair => pair.First.Is(pair.Second));
    }

    private static PairRecord Pair(int index, CorpusResultData? primary, CorpusResultData replica)
    {
        if (primary == null)
            return new PairRecord(index, null, replica, false, false, []);

        var sameNode = primary.NodeId != Guid.Empty && primary.NodeId == replica.NodeId;
        var differing = Differences(primary, replica);
        return new PairRecord(index, primary, replica, sameNode, differing.Count == 0, differing);
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

    private static void WriteSide(Utf8JsonWriter writer, string name, CorpusResultData? result)
    {
        if (result == null)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteStartObject(name);
        writer.WriteString("node_id", result.NodeId == Guid.Empty ? "" : result.NodeId.ToString("D"));
        writer.WriteString("node_name", result.NodeName);
        writer.WriteString("verdict", result.Verdict.ToString());
        writer.WriteString("content_sha256", result.ContentSha256);
        writer.WriteNumber("documents_out", result.Stats?.DocumentsOut ?? 0);
        writer.WriteEndObject();
    }

    #endregion

    #region Types

    /// <summary>A sampled shard: compared only when both sides exist and ran on different nodes.</summary>
    private sealed record PairRecord(int Index, CorpusResultData? Primary, CorpusResultData Replica, bool SameNode, bool Match, List<string> Differing)
    {
        public bool Compared => Primary != null && !SameNode;
    }

    #endregion
}
