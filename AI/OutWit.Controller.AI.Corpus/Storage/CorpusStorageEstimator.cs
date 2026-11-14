using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Storage;

/// <summary>
/// Estimates what a job leaves in the server's blob store from the sizes of its shards and the
/// measured ratios (<see cref="CorpusStorageRatios"/>): per shard its text product and its
/// signatures; the sampled shards twice, since each execution uploads its own products (the
/// largest shards are taken as the sampled ones, so the estimate does not depend on which the
/// platform picks); the report, the integrity and the dedup documents; the inputs. A pipeline that
/// keeps statistics and hashes only leaves no text and no signatures, and no dedup document, so its
/// estimate is the report, the integrity document and the inputs. Nothing is fetched: the sizes are
/// the manifest's, or what the origin answered.
/// </summary>
public static class CorpusStorageEstimator
{
    #region Constants

    private const double BYTES_PER_MIB = 1024.0 * 1024.0;

    /// <summary>The rounding tolerance of the platform's sampled share (Grid.Replicas).</summary>
    private const double SHARE_ROUNDING_TOLERANCE = 1e-9;

    #endregion

    #region Functions

    /// <summary>Estimates a job.</summary>
    /// <param name="sources">The job's shards, with their sizes (a size of 0 or less is unknown).</param>
    /// <param name="pipeline">The pipeline every shard runs through.</param>
    /// <param name="samplingRate">The share of the shards that run twice, 0 to 1.</param>
    /// <param name="inputBytes">The bytes of the manifest and the language model.</param>
    /// <returns>The estimate.</returns>
    public static CorpusStorageEstimate Estimate(IReadOnlyList<CorpusSourceData> sources, CorpusPipelineData pipeline, double samplingRate, long inputBytes)
    {
        var products = new List<long>(sources.Count);
        long text = 0, signatures = 0, documents = 0;
        var unsized = 0;
        var keeps = pipeline.Output == CorpusOutputMode.Full;
        var signs = ShardProcessor.KeepsSignatures(pipeline);
        var minHash = MinHashParameters.Resolve(pipeline.MinHash);

        foreach (var source in sources)
        {
            if (source.SizeBytes <= 0)
            {
                unsized++;
                products.Add(0);
                continue;
            }

            var ratio = CorpusStorageRatios.For(source.Format, pipeline);
            var shardText = keeps ? (long)Math.Ceiling(source.SizeBytes * ratio.TextPerByte) : 0;
            var shardDocuments = (long)Math.Ceiling(source.SizeBytes / BYTES_PER_MIB * ratio.DocumentsPerMib);
            var shardSignatures = signs ? ShardSignatureWriter.HeaderSize(minHash) + shardDocuments * minHash.SignatureBytes : 0;

            text += shardText;
            signatures += shardSignatures;
            documents += shardDocuments;
            products.Add(shardText + shardSignatures);
        }

        var sampled = SampledShards(sources.Count, samplingRate);
        var replicas = products.OrderByDescending(bytes => bytes).Take(sampled).Sum();

        return new CorpusStorageEstimate
        {
            TextBytes = text,
            SignatureBytes = signatures,
            ReplicaBytes = replicas,
            DocumentBytes = DocumentBytes(sources.Count, sampled, documents, signs),
            InputBytes = Math.Max(0, inputBytes),
            Documents = documents,
            SampledShards = sampled,
            UnsizedSources = unsized,
            ProductsKept = keeps
        };
    }

    /// <summary>How many shards run twice: the platform's rule (a share above 0 selects at least one item, rounded up).</summary>
    /// <param name="shards">The job's shards.</param>
    /// <param name="samplingRate">The requested share; clamped to 0..1.</param>
    /// <returns>The sampled shards.</returns>
    public static int SampledShards(int shards, double samplingRate)
    {
        if (shards <= 0 || !(samplingRate > 0))
            return 0;

        var selected = (int)Math.Ceiling(shards * Math.Min(samplingRate, 1.0) - SHARE_ROUNDING_TOLERANCE);
        return Math.Clamp(selected, 1, shards);
    }

    private static long DocumentBytes(int shards, int sampled, long documents, bool sign)
    {
        if (shards == 0)
            return 0;

        long bytes = CorpusStorageRatios.REPORT_BASE_BYTES + (long)CorpusStorageRatios.REPORT_BYTES_PER_SHARD * shards;
        bytes += CorpusStorageRatios.INTEGRITY_BASE_BYTES + (long)CorpusStorageRatios.INTEGRITY_BYTES_PER_PAIR * sampled;

        if (sign)
            bytes += CorpusStorageRatios.DEDUP_BASE_BYTES + (long)CorpusStorageRatios.DEDUP_BYTES_PER_SHARD * shards + CorpusStorageRatios.DEDUP_BYTES_PER_DOCUMENT * documents;

        return bytes;
    }

    #endregion
}
