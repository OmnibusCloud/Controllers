using OutWit.Controller.AI.Corpus.Delivery;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Storage;

/// <summary>
/// Estimates what a job leaves in the server's blob store from the sizes of its shards and the
/// measured ratios (<see cref="CorpusStorageRatios"/>): per shard its text product and its
/// signatures; the sampled packages twice, since each execution uploads its own products (the
/// largest are taken as the sampled ones, so the estimate does not depend on which the platform
/// picks); the report, the integrity and the dedup documents; the task and result packages; the
/// inputs. A pipeline that keeps statistics and hashes only leaves no text and no signatures, and
/// no dedup document, so its estimate is the report, the integrity document, the packages and the
/// inputs. Nothing is fetched: the sizes are the manifest's, or what the origin answered; a shard
/// whose size nobody knows is counted at the cap the nodes hold it to, so that a manifest
/// without sizes cannot pass a headroom it would overrun.
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
    /// <param name="samplingRate">The share of the packages that run twice, 0 to 1.</param>
    /// <param name="inputBytes">The bytes of the manifest and the language model.</param>
    /// <param name="capBytes">The size a shard of unknown size is counted at: the cap the nodes hold it to. 0 leaves such shards out of the estimate (and the estimate says how many).</param>
    /// <param name="measuredThresholds">Whether the language model's rule data is the one the ratios were measured with (<see cref="CorpusStorageRatios.For"/>).</param>
    /// <returns>The estimate.</returns>
    public static CorpusStorageEstimate Estimate(IReadOnlyList<CorpusSourceData> sources, CorpusPipelineData pipeline, double samplingRate, long inputBytes, long capBytes = 0, bool measuredThresholds = true)
    {
        var products = new List<long>(sources.Count);
        long text = 0, signatures = 0, documents = 0;
        var unsized = 0;
        var keeps = pipeline.Output == CorpusOutputMode.Full;
        var signs = ShardProcessor.KeepsSignatures(pipeline);
        var minHash = MinHashParameters.Resolve(pipeline.MinHash);

        foreach (var source in sources)
        {
            var size = source.SizeBytes;
            if (size <= 0)
            {
                unsized++;
                if (capBytes <= 0)
                {
                    products.Add(0);
                    continue;
                }

                size = capBytes;
            }

            var ratio = CorpusStorageRatios.For(source.Format, pipeline, measuredThresholds);
            var shardText = keeps ? (long)Math.Ceiling(size * ratio.TextPerByte) : 0;
            var shardDocuments = (long)Math.Ceiling(size / BYTES_PER_MIB * ratio.DocumentsPerMib);
            var shardSignatures = signs ? ShardSignatureWriter.HeaderSize(minHash) + shardDocuments * minHash.SignatureBytes : 0;

            text += shardText;
            signatures += shardSignatures;
            documents += shardDocuments;
            products.Add(shardText + shardSignatures);
        }

        // The platform samples packages: the largest are taken as the sampled ones.
        var perPackage = CorpusPackagePlanner.ShardsPerPackage(sources.Count);
        var packages = CorpusPackagePlanner.PackageSums(products);
        var sampled = SampledShards(packages.Count, samplingRate);
        var replicas = packages.OrderByDescending(bytes => bytes).Take(sampled).Sum();
        var sampledShards = Math.Min(sources.Count, sampled * perPackage);

        return new CorpusStorageEstimate
        {
            TextBytes = text,
            SignatureBytes = signatures,
            ReplicaBytes = replicas,
            DocumentBytes = DocumentBytes(sources.Count, sampledShards, documents, signs),
            PackageBytes = TaskPackageBytes(sources.Count, packages.Count) + ResultPackageBytes(sources.Count, packages.Count, sampledShards, sampled),
            ResultPackageBytes = ResultPackageBytes(sources.Count, packages.Count, sampledShards, sampled),
            InputBytes = Math.Max(0, inputBytes),
            Documents = documents,
            SampledShards = sampledShards,
            UnsizedSources = unsized,
            UnsizedAtCap = capBytes > 0,
            ProductsKept = keeps,
            MeasuredFilter = !pipeline.FilterQuality || CorpusStorageRatios.FiltersAsMeasured(pipeline, measuredThresholds)
        };
    }

    /// <summary>How many items run twice: the platform's rule (a share above 0 selects at least one item, rounded up). The items are the job's packages: its shards, up to <see cref="CorpusPackagePlanner.MAX_PACKAGES"/> of them.</summary>
    /// <param name="shards">The job's items.</param>
    /// <param name="samplingRate">The requested share; clamped to 0..1.</param>
    /// <returns>The sampled shards.</returns>
    public static int SampledShards(int shards, double samplingRate)
    {
        if (shards <= 0 || !(samplingRate > 0))
            return 0;

        var selected = (int)Math.Ceiling(shards * Math.Min(samplingRate, 1.0) - SHARE_ROUNDING_TOLERANCE);
        return Math.Clamp(selected, 1, shards);
    }

    /// <summary>The task packages of the first round: what the server writes for the nodes.</summary>
    private static long TaskPackageBytes(int shards, int packages)
    {
        return shards == 0 ? 0 : (long)CorpusStorageRatios.PACKAGE_BASE_BYTES * packages + (long)CorpusStorageRatios.TASK_PACKAGE_BYTES_PER_SHARD * shards;
    }

    /// <summary>The result packages of every execution: what the nodes upload beside the products.</summary>
    private static long ResultPackageBytes(int shards, int packages, int sampledShards, int sampledPackages)
    {
        return shards == 0 ? 0 : (long)CorpusStorageRatios.PACKAGE_BASE_BYTES * (packages + sampledPackages) + (long)CorpusStorageRatios.RESULT_PACKAGE_BYTES_PER_SHARD * ((long)shards + sampledShards);
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
