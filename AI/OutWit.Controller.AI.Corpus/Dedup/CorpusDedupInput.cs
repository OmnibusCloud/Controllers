using System.Text.Json;
using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Signatures;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Dedup;

/// <summary>
/// What the near-duplicate merge reads from a run's report: the completed shards' signature blobs
/// with what the report says each holds (its hash, its document count, its MinHash parameters), or
/// the reason the run cannot be merged. The report is a lab's input and is read as one: any shape
/// but a report's is refused by name, a report that names one shard or one blob twice is refused,
/// and the documents are added up from the report and held to the merge's capacity here, before a
/// single blob is opened. A run that kept statistics and hashes only, or did not sign, has no
/// signatures. A shard whose signatures were made with other parameters than the run's is left
/// out and named, not a reason to refuse the rest. A report of a controller before the parameters
/// were settings names none, and its signatures carry the defaults.
/// </summary>
/// <param name="Shards">The completed shards with signatures, in index order.</param>
/// <param name="Parameters">The parameters they were made with (the defaults when there is none).</param>
/// <param name="Refusal">Why the merge cannot run, or null.</param>
public sealed record CorpusDedupInput(IReadOnlyList<CorpusDedupSource> Shards, MinHashParameters Parameters, string? Refusal)
{
    #region Constants

    /// <summary>The most bytes a report may have to be read by the merge.</summary>
    public const long MAX_REPORT_BYTES = 256L * 1024 * 1024;

    #endregion

    #region Functions

    /// <summary>Reads a report.</summary>
    /// <param name="report">The report bytes (<c>corpus-report/3</c> or an earlier schema).</param>
    /// <returns>The input, with the refusal when the run cannot be merged.</returns>
    /// <exception cref="InvalidOperationException">Not a corpus report.</exception>
    public static CorpusDedupInput Read(byte[] report)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(report);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"The report is not a {CorpusReportWriter.SCHEMA} document.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Text(root, "schema") is not { } schema
                || (schema != CorpusReportWriter.SCHEMA && !CorpusReportWriter.PREVIOUS_SCHEMAS.Contains(schema))
                || !root.TryGetProperty("shards", out var shardList) || shardList.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException($"The report is not a {CorpusReportWriter.SCHEMA} document.");

            if (root.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.Object && Text(summary, "output") == CorpusOutputModeNames.STATS_AND_HASHES)
            {
                return Refuse(
                    $"The run kept statistics and hashes only (pipeline output \"{CorpusOutputModeNames.STATS_AND_HASHES}\"): it has no signatures, and the near-duplicate merge needs them. " +
                    $"Run the shards again with \"output\": \"{CorpusOutputModeNames.FULL}\" to deduplicate them.");
            }

            var shards = new List<CorpusDedupSource>();
            var indexes = new HashSet<int>();
            var blobs = new HashSet<Guid>();
            foreach (var shard in shardList.EnumerateArray())
            {
                if (shard.ValueKind != JsonValueKind.Object)
                    return Refuse("The report lists a shard that is not an object.");

                if (Text(shard, "verdict") != CorpusShardVerdict.Completed.ToString())
                    continue;

                if (Number(shard, "index") is not { } index || index < 0)
                    return Refuse("The report lists a completed shard without an index.");

                var documents = Documents(shard);
                if (documents == null)
                    return Refuse($"Shard {index} completed and its statistics state no document count.");

                if (!Guid.TryParse(Text(shard, "signatures_blob"), out var blobId) || blobId == Guid.Empty)
                {
                    if (documents > 0)
                        return Refuse($"Shard {index} completed with documents and no signatures (the pipeline's sign was false): the near-duplicate merge needs the signatures of every shard.");
                    continue;
                }

                // A report before 0.3.0 names no hash; one that names a hash names a SHA-256.
                var sha256 = shard.TryGetProperty("signatures_sha256", out var hash) ? hash.ValueKind == JsonValueKind.String ? hash.GetString() : null : "";
                if (!CorpusText.IsSha256OrEmpty(sha256))
                    return Refuse($"Shard {index}: 'signatures_sha256' is not a SHA-256.");

                // One blob read many times is many times its memory; a shard listed twice is two masks for one product.
                if (!indexes.Add(index))
                    return Refuse($"The report lists shard {index} more than once.");

                if (!blobs.Add(blobId))
                    return Refuse($"The report names the signatures blob of shard {index} more than once: every shard has a blob of its own.");

                var parameters = ShardParameters(shard);
                if (MinHashParameters.Problem(parameters.ToData()) != null)
                    return Refuse($"Shard {index}: the MinHash parameters the report states cannot be used.");

                shards.Add(new CorpusDedupSource(index, blobId, parameters, sha256!, documents.Value));
            }

            shards.Sort((first, second) => first.Index.CompareTo(second.Index));

            // The run's parameters are those of most of its shards (of the first, between equals):
            // an honest report has one set; a shard with another is left out, not the run.
            var runParameters = shards
                .GroupBy(shard => shard.Parameters)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Min(shard => shard.Index))
                .Select(group => group.Key)
                .FirstOrDefault() ?? MinHashParameters.DEFAULT;

            var excluded = shards.Where(shard => shard.Parameters != runParameters).Select(shard => new CorpusDedupExclusion(shard.Index, shard.SignaturesBlobId, CorpusDedupExclusion.PARAMETERS)).ToList();
            var merged = shards.Where(shard => shard.Parameters == runParameters).ToList();

            // Before a blob is opened: the documents the report states, against what one merge holds.
            var total = merged.Sum(shard => (long)shard.Documents);
            var capacity = CorpusDeduplicator.Capacity(runParameters.Hashes);
            if (total > capacity)
            {
                return Refuse(
                    $"The run has {total} documents and one merge holds {capacity} with {runParameters.Hashes}-hash signatures: " +
                    "merge the run in parts (a report of some of its shards), or sign with fewer hashes.") with { Parameters = runParameters };
            }

            return new CorpusDedupInput(merged, runParameters, null) { Excluded = excluded };
        }
    }

    private static CorpusDedupInput Refuse(string refusal)
    {
        return new CorpusDedupInput([], MinHashParameters.DEFAULT, refusal);
    }

    private static MinHashParameters ShardParameters(JsonElement shard)
    {
        if (!shard.TryGetProperty("minhash", out var minHash) || minHash.ValueKind != JsonValueKind.Object)
            return MinHashParameters.DEFAULT;

        return MinHashParameters.Resolve(new CorpusMinHashData
        {
            Hashes = Number(minHash, "hashes") ?? 0,
            ShingleWords = Number(minHash, "shingle_words") ?? 0,
            Bands = Number(minHash, "bands") ?? 0
        });
    }

    private static int? Documents(JsonElement shard)
    {
        if (!shard.TryGetProperty("stats", out var stats) || stats.ValueKind != JsonValueKind.Object)
            return null;

        return Number(stats, "documents_out") is { } documents and >= 0 ? documents : null;
    }

    private static string? Text(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static int? Number(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
    }

    #endregion

    #region Properties

    /// <summary>The completed shards left out for their MinHash parameters; in index order.</summary>
    public IReadOnlyList<CorpusDedupExclusion> Excluded { get; init; } = [];

    #endregion
}
