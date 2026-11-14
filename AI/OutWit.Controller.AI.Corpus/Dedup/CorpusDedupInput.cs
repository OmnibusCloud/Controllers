using System.Text.Json;
using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Dedup;

/// <summary>
/// What the near-duplicate merge reads from a run's report: the completed shards' signature blobs
/// and the MinHash parameters they were made with, or the reason the run cannot be merged. A run
/// that kept statistics and hashes only, or did not sign, has no signatures; shards whose
/// signatures were made with different parameters cannot be compared. A report of a controller
/// before the parameters were settings names none, and its signatures carry the defaults.
/// </summary>
/// <param name="Shards">The completed shards with signatures, in report order.</param>
/// <param name="Parameters">The parameters every one of them was made with (the defaults when there is none).</param>
/// <param name="Refusal">Why the merge cannot run, or null.</param>
public sealed record CorpusDedupInput(IReadOnlyList<CorpusDedupSource> Shards, MinHashParameters Parameters, string? Refusal)
{
    #region Functions

    /// <summary>Reads a report.</summary>
    /// <param name="report">The report bytes (<c>corpus-report/2</c> or <c>corpus-report/1</c>).</param>
    /// <returns>The input, with the refusal when the run cannot be merged.</returns>
    /// <exception cref="InvalidOperationException">Not a corpus report.</exception>
    public static CorpusDedupInput Read(byte[] report)
    {
        using var document = JsonDocument.Parse(report);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema", out var schema) || schema.GetString() is not (CorpusReportWriter.SCHEMA or CorpusReportWriter.PREVIOUS_SCHEMA))
            throw new InvalidOperationException($"The report is not a {CorpusReportWriter.SCHEMA} document.");

        if (root.TryGetProperty("summary", out var summary) && summary.TryGetProperty("output", out var output) && output.GetString() == CorpusOutputModeNames.STATS_AND_HASHES)
        {
            return new CorpusDedupInput([], MinHashParameters.DEFAULT,
                $"The run kept statistics and hashes only (pipeline output \"{CorpusOutputModeNames.STATS_AND_HASHES}\"): it has no signatures, and the near-duplicate merge needs them. " +
                $"Run the shards again with \"output\": \"{CorpusOutputModeNames.FULL}\" to deduplicate them.");
        }

        var shards = new List<CorpusDedupSource>();
        foreach (var shard in root.GetProperty("shards").EnumerateArray())
        {
            if (shard.GetProperty("verdict").GetString() != CorpusShardVerdict.Completed.ToString())
                continue;

            var index = shard.GetProperty("index").GetInt32();
            if (!Guid.TryParse(shard.GetProperty("signatures_blob").GetString(), out var blobId) || blobId == Guid.Empty)
            {
                if (DocumentsOut(shard) > 0)
                    return new CorpusDedupInput([], MinHashParameters.DEFAULT,
                        $"Shard {index} completed with documents and no signatures (the pipeline's sign was false): the near-duplicate merge needs the signatures of every shard.");
                continue;
            }

            shards.Add(new CorpusDedupSource(index, blobId, ShardParameters(shard)));
        }

        var parameters = shards.Count > 0 ? shards[0].Parameters : MinHashParameters.DEFAULT;
        var different = shards.FirstOrDefault(shard => shard.Parameters != parameters);
        if (different != null)
        {
            return new CorpusDedupInput([], parameters,
                $"The shards' signatures were made with different MinHash parameters (shard {shards[0].Index}: {parameters.Describe()}; shard {different.Index}: {different.Parameters.Describe()}): signatures made differently cannot be compared.");
        }

        return new CorpusDedupInput(shards, parameters, null);
    }

    /// <summary>Checks a shard's signatures blob against what the report says it holds.</summary>
    /// <param name="source">The shard as the report names it.</param>
    /// <param name="blob">The blob's bytes (at least its header).</param>
    /// <returns>The refusal when the blob was made otherwise, or null.</returns>
    public static string? Check(CorpusDedupSource source, ReadOnlySpan<byte> blob)
    {
        var (hashes, shingleWords, _) = ShardSignatureWriter.ReadHeader(blob);
        if (hashes == source.Parameters.Hashes && shingleWords == source.Parameters.ShingleWords)
            return null;

        return $"Shard {source.Index}'s signatures blob holds {hashes} hashes over {shingleWords}-word shingles, the report says {source.Parameters.Hashes} over {source.Parameters.ShingleWords}: signatures made differently cannot be compared.";
    }

    private static MinHashParameters ShardParameters(JsonElement shard)
    {
        if (!shard.TryGetProperty("minhash", out var minHash) || minHash.ValueKind != JsonValueKind.Object)
            return MinHashParameters.DEFAULT;

        return MinHashParameters.Resolve(new CorpusMinHashData
        {
            Hashes = Int(minHash, "hashes"),
            ShingleWords = Int(minHash, "shingle_words"),
            Bands = Int(minHash, "bands")
        });
    }

    private static int DocumentsOut(JsonElement shard)
    {
        return shard.TryGetProperty("stats", out var stats) && stats.ValueKind == JsonValueKind.Object && stats.TryGetProperty("documents_out", out var documents)
            ? documents.GetInt32()
            : 0;
    }

    private static int Int(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
    }

    #endregion
}
