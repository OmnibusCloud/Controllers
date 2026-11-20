using System.Globalization;
using OutWit.Controller.AI.Corpus.Delivery;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Storage;

namespace OutWit.Controller.AI.Corpus.Manifests;

/// <summary>
/// What the contributors' links will carry for a job, part by part:
/// <list type="bullet">
/// <item>the fetches: every shard once, and the packages sampled for integrity a second time, each
/// by a machine of its own (the platform picks them at random; the estimate takes the largest, as
/// the storage estimate does, so it does not depend on the pick and errs on the side of too much);</item>
/// <item>the uploads: the text and the signatures every execution sends to the server, from the
/// measured ratios of product to shard bytes, and the result packages;</item>
/// <item>the language model, which every node that takes part downloads once, when the number of
/// nodes is known;</item>
/// <item>an allowance (<see cref="RETRY_ALLOWANCE"/> of the fetches) for fetches that start over
/// and for shards fetched again in a repair or a dispute.</item>
/// </list>
/// Beside the estimate stands a ceiling: what the nodes accept for the same fetches at the most,
/// every shard at the size it is held to. The estimate is what is expected of an honest manifest;
/// the ceiling holds whoever wrote it.
/// </summary>
/// <param name="OnceBytes">Every shard's size once (a size of 0 or less is not known and not counted).</param>
/// <param name="CopyBytes">The second fetch of the sampled shards.</param>
/// <param name="SampledShards">How many shards run twice.</param>
public sealed record CorpusTrafficEstimate(long OnceBytes, long CopyBytes, int SampledShards)
{
    #region Constants

    /// <summary>
    /// The share of the fetches added for what a run fetches more than once without anything being
    /// wrong with the manifest: an attempt that starts over when the origin does not continue a
    /// broken body, a shard a node returned as a fault of its own (fetched once more, by two
    /// machines when the run samples), a shard whose copies disagreed (fetched by up to three more
    /// machines). A tenth: an allowance,
    /// not a bound — a node that loses its whole share has it fetched again elsewhere.
    /// </summary>
    public const double RETRY_ALLOWANCE = 1.0 / RETRY_ALLOWANCE_DIVISOR;

    /// <summary>The allowance as the estimate computes it, in whole bytes: the fetches over this number, rounded up.</summary>
    public const int RETRY_ALLOWANCE_DIVISOR = 10;

    #endregion

    #region Functions

    /// <summary>Estimates the fetches of a job from its shards' sizes: nothing is known of uploads here.</summary>
    /// <param name="sizes">Each shard's size on the wire (0 or less: not known).</param>
    /// <param name="samplingRate">The share of the packages that run twice, 0 to 1.</param>
    /// <returns>The estimate.</returns>
    public static CorpusTrafficEstimate Of(IReadOnlyList<long> sizes, double samplingRate)
    {
        return Of(sizes, samplingRate, null, 0, 0, 0);
    }

    /// <summary>Estimates the traffic of a job.</summary>
    /// <param name="sizes">Each shard's size on the wire as it is counted (0 or less: not known, not counted).</param>
    /// <param name="samplingRate">The share of the packages that run twice, 0 to 1.</param>
    /// <param name="bounds">The most a node accepts for each shard (<see cref="ShardLimits.FetchCap"/>), or null when no ceiling is asked for.</param>
    /// <param name="uploadBytes">What every execution uploads, together (the storage estimate's products and result packages).</param>
    /// <param name="modelBytes">The language model's size; 0 when the job has none.</param>
    /// <param name="nodes">The nodes that will take part; 0 when not known (the model is then left out and said).</param>
    /// <param name="uploadCeilingBytes">The most every execution may upload together (the storage estimate's product ceiling and result packages); 0 when not known.</param>
    /// <returns>The estimate.</returns>
    public static CorpusTrafficEstimate Of(IReadOnlyList<long> sizes, double samplingRate, IReadOnlyList<long>? bounds, long uploadBytes, long modelBytes, int nodes, long uploadCeilingBytes = 0)
    {
        var perPackage = CorpusPackagePlanner.ShardsPerPackage(sizes.Count);
        var packages = CorpusPackagePlanner.PackageSums(sizes);
        var sampled = CorpusStorageEstimator.SampledShards(packages.Count, samplingRate);

        long ceiling = 0;
        if (bounds != null)
        {
            var bounded = CorpusPackagePlanner.PackageSums(bounds);
            ceiling = bounded.Sum() + bounded.OrderByDescending(bytes => bytes).Take(sampled).Sum();
        }

        return new CorpusTrafficEstimate(packages.Sum(), packages.OrderByDescending(bytes => bytes).Take(sampled).Sum(), Math.Min(sizes.Count, sampled * perPackage))
        {
            SampledPackages = sampled,
            ShardsPerPackage = perPackage,
            UploadBytes = Math.Max(0, uploadBytes),
            ModelBytesPerNode = Math.Max(0, modelBytes),
            Nodes = Math.Max(0, nodes),
            CeilingBytes = ceiling,
            UploadCeilingBytes = Math.Max(0, uploadCeilingBytes)
        };
    }

    /// <summary>The preflight's line.</summary>
    /// <returns>For example "Contributor traffic estimate 2493 bytes: fetches 1700 (every shard once 1000 and the second fetch of the 2 largest shard(s), sampled for integrity, 700), uploads 623 (the products and the result packages of every execution) and an allowance of 170 ...".</returns>
    public string Describe()
    {
        var fetches = string.Create(CultureInfo.InvariantCulture, $"fetches {FetchBytes} (every shard once {OnceBytes}");
        if (SampledPackages > 0)
        {
            fetches += ShardsPerPackage > 1
                ? string.Create(CultureInfo.InvariantCulture, $" and the second fetch of the {SampledPackages} largest package(s), {SampledShards} shard(s), sampled for integrity, {CopyBytes}")
                : string.Create(CultureInfo.InvariantCulture, $" and the second fetch of the {SampledShards} largest shard(s), sampled for integrity, {CopyBytes}");
        }

        fetches += ")";

        var parts = new List<string> { fetches, string.Create(CultureInfo.InvariantCulture, $"uploads {UploadBytes} (the products and the result packages of every execution)") };
        if (ModelBytes > 0)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"the language model {ModelBytes} ({Nodes} node(s), once each)"));

        return string.Create(CultureInfo.InvariantCulture, $"Contributor traffic estimate {TotalBytes} bytes: {string.Join(", ", parts)} and an allowance of {RetryBytes} (a tenth of the fetches) for fetches that start over and for shards fetched again in a repair or a dispute.");
    }

    /// <summary>
    /// What the estimate leaves out, said beside it: the cases no fixed allowance covers. A node
    /// that fails has its whole share fetched again elsewhere; a run with many disputes or repairs
    /// goes past the allowance; and without the number of nodes the language model is not counted.
    /// </summary>
    /// <returns>The preflight's second line on traffic.</returns>
    public string DescribeRounds()
    {
        // A run that samples sends a shard out again on two machines, and such a pair can
        // disagree like a sampled one: no count of the sampled shards bounds the disputes.
        var line = "Not in the traffic estimate: a node that fails or falls silent has its whole share fetched again by other nodes. " +
                   "The allowance is not a bound: " +
                   (SampledShards > 0
                       ? $"a shard whose two copies disagree is fetched again by up to {Trust.CorpusLedger.DISPUTE_COPIES} machines (a dispute; {SampledShards} shard(s) are sampled here), and " +
                         $"a shard a node returns as a fault of its own is fetched again by {Trust.CorpusLedger.REPAIR_COPIES} machines (a repair)"
                       : "a shard a node returns as a fault of its own is fetched once more (a repair)") +
                   "; the report's counters (repairs, disputes) say what a run added.";

        if (ModelBytesPerNode > 0 && Nodes == 0)
            line += string.Create(CultureInfo.InvariantCulture, $" The number of nodes is not known here: every node that takes part also downloads the language model once ({ModelBytesPerNode} bytes).");

        return line;
    }

    /// <summary>The preflight's line on what the nodes enforce.</summary>
    /// <param name="unsizedShards">Shards with no size of their own: held to the cap only.</param>
    /// <param name="capBytes">The job's byte cap.</param>
    /// <returns>For example "The nodes accept at most 2125 bytes for those fetches ...".</returns>
    public string DescribeCeiling(int unsizedShards, long capBytes)
    {
        var line = string.Create(CultureInfo.InvariantCulture,
            $"The nodes accept at most {CeilingBytes} bytes for those fetches, whoever wrote the manifest: a shard is held to the size the manifest declares (or the origin answered when the server asked) and 1/{ShardLimits.DECLARED_SLACK_DIVISOR} more, {ShardLimits.DECLARED_SLACK_MIN_BYTES} bytes at least; one past that is refused as Oversized ({Trust.CorpusResultCodes.OVERSIZED_DECLARED}).");

        if (UploadCeilingBytes > 0)
            line += string.Create(CultureInfo.InvariantCulture,
                $" They upload at most {UploadCeilingBytes} bytes for them: a shard's product is held to {ShardProductLimits.DOCUMENTS_PER_MIB} documents a fetched mebibyte ({ShardProductLimits.MIN_DOCUMENTS} at least) and {ShardProductLimits.TEXT_PER_BYTE} bytes of compressed text a fetched byte ({ShardProductLimits.MIN_TEXT_BYTES} at least), and one past that is refused as Oversized ({Trust.CorpusResultCodes.OVERSIZED_PRODUCT}).");

        if (unsizedShards > 0)
            line += string.Create(CultureInfo.InvariantCulture, $" {unsizedShards} shard(s) have no size of their own and are held to the cap of {capBytes} bytes only: declare their sizes to make the estimate a bound for them.");

        return line;
    }

    #endregion

    #region Properties

    /// <summary>The packages that run twice (a package is a shard up to <see cref="CorpusPackagePlanner.MAX_PACKAGES"/> shards a job).</summary>
    public int SampledPackages { get; init; }

    /// <summary>The shards a package of the job holds.</summary>
    public int ShardsPerPackage { get; init; } = 1;

    /// <summary>What every execution uploads, together.</summary>
    public long UploadBytes { get; init; }

    /// <summary>The language model's size; 0 when the job has none.</summary>
    public long ModelBytesPerNode { get; init; }

    /// <summary>The nodes that will take part; 0 when not known.</summary>
    public int Nodes { get; init; }

    /// <summary>The most the nodes accept for the fetches: every shard at the size it is held to; 0 when no ceiling was asked for.</summary>
    public long CeilingBytes { get; init; }

    /// <summary>The most every execution may upload together: the products at the most a shard may give and the result packages; 0 when not known.</summary>
    public long UploadCeilingBytes { get; init; }

    /// <summary>The fetches: every shard once and the sampled ones a second time.</summary>
    public long FetchBytes => OnceBytes + CopyBytes;

    /// <summary>The language model as every node that takes part downloads it; 0 when the nodes are not known.</summary>
    public long ModelBytes => ModelBytesPerNode * Nodes;

    /// <summary>The allowance: <see cref="RETRY_ALLOWANCE"/> of the fetches, rounded up.</summary>
    public long RetryBytes => (FetchBytes + RETRY_ALLOWANCE_DIVISOR - 1) / RETRY_ALLOWANCE_DIVISOR;

    /// <summary>Everything together: the traffic promise.</summary>
    public long TotalBytes => FetchBytes + UploadBytes + ModelBytes + RetryBytes;

    #endregion
}
