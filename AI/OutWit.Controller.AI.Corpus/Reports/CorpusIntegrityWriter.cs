using System.Text.Json;
using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Reports;

/// <summary>
/// Writes the integrity document of a run: every shard executed more than once, its executions
/// paired, and whether they agree on everything an honest node cannot
/// vary — the verdict, the content hash of the product, the document count, the drop and
/// language histograms. The platform keeps the executions of one shard on different machines,
/// and the document does not say which. Two kinds of pair are counted, not compared: one whose
/// sides ran different controller versions (a rollout in progress: a stage may have changed
/// between them), and one whose side did not finish for a reason of its machine or its link (a
/// fetch that failed, a model that did not arrive, an error on the node) — a mismatch says two
/// finished executions disagree, never that one of them stopped.
/// Schema <c>corpus-integrity/3</c>.
/// </summary>
public static class CorpusIntegrityWriter
{
    #region Constants

    /// <summary>The document schema name.</summary>
    public const string SCHEMA = "corpus-integrity/3";

    /// <summary>
    /// The verdicts of an execution that did not finish for a reason of its machine or its link.
    /// The others are what the input makes of the shard (completed, refused, oversized,
    /// malformed) and are the same on every honest node.
    /// </summary>
    public static readonly IReadOnlySet<CorpusShardVerdict> UNFINISHED = new HashSet<CorpusShardVerdict>
    {
        CorpusShardVerdict.FetchFailed,
        CorpusShardVerdict.PipelineUnavailable,
        CorpusShardVerdict.Failed
    };

    #endregion

    #region Functions

    /// <summary>Serializes the pairs as UTF-8 JSON.</summary>
    /// <param name="executions">All executions of the run, any order; the shards executed once make no pair.</param>
    /// <returns>The JSON document bytes.</returns>
    public static byte[] Write(IReadOnlyList<CorpusResultData> executions)
    {
        var pairs = executions
            .GroupBy(execution => execution.TaskIndex)
            .OrderBy(group => group.Key)
            .SelectMany(PairsOf)
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
            writer.WriteNumber("version_skew", pairs.Count(pair => pair.VersionSkew));
            writer.WriteNumber("incomplete", pairs.Count(pair => pair.Incomplete));
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
        // The breakdown of language_unsupported is part of the drops: a function of the shard.
        if (!SameCounts(primaryStats.Drops, replicaStats.Drops) || !SameCounts(primaryStats.UnsupportedLanguages, replicaStats.UnsupportedLanguages))
            fields.Add("drops");
        if (!SameCounts(primaryStats.Languages, replicaStats.Languages))
            fields.Add("languages");

        return fields;
    }

    private static bool SameCounts(IReadOnlyList<CorpusCountData> first, IReadOnlyList<CorpusCountData> second)
    {
        return first.Count == second.Count && first.Zip(second).All(pair => pair.First.Is(pair.Second));
    }

    private static IEnumerable<PairRecord> PairsOf(IGrouping<int, CorpusResultData> shard)
    {
        var primary = CorpusExecutions.Pick(shard);

        return shard
            .Where(execution => !ReferenceEquals(execution, primary))
            .OrderBy(execution => execution.ContentSha256, StringComparer.Ordinal)
            .Select(execution => Pair(shard.Key, primary, execution));
    }

    private static PairRecord Pair(int index, CorpusResultData primary, CorpusResultData replica)
    {
        var versionSkew = primary.ControllerVersion.Length > 0 && replica.ControllerVersion.Length > 0
                          && !string.Equals(primary.ControllerVersion, replica.ControllerVersion, StringComparison.Ordinal);
        var incomplete = !versionSkew && (UNFINISHED.Contains(primary.Verdict) || UNFINISHED.Contains(replica.Verdict));
        var differing = Differences(primary, replica);
        return new PairRecord(index, primary, replica, versionSkew, incomplete, differing.Count == 0, differing);
    }

    private static void WritePair(Utf8JsonWriter writer, PairRecord pair)
    {
        writer.WriteStartObject();
        writer.WriteNumber("index", pair.Index);
        WriteSide(writer, "primary", pair.Primary);
        WriteSide(writer, "replica", pair.Replica);
        writer.WriteBoolean("compared", pair.Compared);
        if (pair.Incomplete)
            writer.WriteBoolean("incomplete", true);
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

    private static void WriteSide(Utf8JsonWriter writer, string name, CorpusResultData result)
    {
        writer.WriteStartObject(name);
        writer.WriteString("controller", result.ControllerVersion);
        writer.WriteString("verdict", result.Verdict.ToString());
        writer.WriteString("content_sha256", result.ContentSha256);
        writer.WriteNumber("documents_out", result.Stats?.DocumentsOut ?? 0);
        writer.WriteEndObject();
    }

    #endregion

    #region Types

    /// <summary>Two executions of one shard: compared only when they ran the same controller version and both finished.</summary>
    private sealed record PairRecord(int Index, CorpusResultData Primary, CorpusResultData Replica, bool VersionSkew, bool Incomplete, bool Match, List<string> Differing)
    {
        public bool Compared => !VersionSkew && !Incomplete;
    }

    #endregion
}
