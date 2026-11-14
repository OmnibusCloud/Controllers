using System.Globalization;
using OutWit.Controller.AI.Corpus.Storage;

namespace OutWit.Controller.AI.Corpus.Manifests;

/// <summary>
/// What the contributors will fetch: every shard once, and the shards sampled for integrity a
/// second time, each by a machine of its own. The platform picks the sampled shards at random; the
/// estimate takes the largest ones, as the storage estimate does, so it does not depend on the pick
/// and errs on the side of too much.
/// </summary>
/// <param name="OnceBytes">Every shard's size once (a size of 0 or less is not known and not counted).</param>
/// <param name="CopyBytes">The second fetch of the sampled shards.</param>
/// <param name="SampledShards">How many shards run twice.</param>
public sealed record CorpusTrafficEstimate(long OnceBytes, long CopyBytes, int SampledShards)
{
    #region Functions

    /// <summary>Estimates the traffic of a job.</summary>
    /// <param name="sizes">Each shard's size on the wire (0 or less: not known).</param>
    /// <param name="samplingRate">The share of the shards that run twice, 0 to 1.</param>
    /// <returns>The estimate.</returns>
    public static CorpusTrafficEstimate Of(IReadOnlyList<long> sizes, double samplingRate)
    {
        var sampled = CorpusStorageEstimator.SampledShards(sizes.Count, samplingRate);
        var known = sizes.Where(size => size > 0).ToList();

        return new CorpusTrafficEstimate(known.Sum(), known.OrderByDescending(size => size).Take(sampled).Sum(), sampled);
    }

    /// <summary>The preflight's line.</summary>
    /// <returns>For example "Contributor traffic estimate 1993119664 bytes: every shard once (1584054229) and the second fetch of the 6 largest shards, sampled for integrity (409065435)."</returns>
    public string Describe()
    {
        var line = string.Create(CultureInfo.InvariantCulture, $"Contributor traffic estimate {TotalBytes} bytes: every shard once ({OnceBytes})");
        return SampledShards > 0
            ? line + string.Create(CultureInfo.InvariantCulture, $" and the second fetch of the {SampledShards} largest shard(s), sampled for integrity ({CopyBytes}).")
            : line + ".";
    }

    #endregion

    #region Properties

    /// <summary>Everything together: the traffic promise.</summary>
    public long TotalBytes => OnceBytes + CopyBytes;

    #endregion
}
