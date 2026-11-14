using System.Globalization;

namespace OutWit.Controller.AI.Corpus.Storage;

/// <summary>
/// Puts a storage estimate next to the deployment's limit: the line a preflight shows, and the
/// refusal when the job would leave more on the server than the deployment allows one job.
/// A deployment that states no headroom refuses nothing.
/// </summary>
public static class CorpusStorageCheck
{
    #region Constants

    private const double BYTES_PER_MIB = 1024.0 * 1024.0;
    private const double BYTES_PER_GIB = BYTES_PER_MIB * 1024.0;

    #endregion

    #region Functions

    /// <summary>The refusal, or null when the job fits or no headroom is stated.</summary>
    /// <param name="estimate">The job's estimate.</param>
    /// <param name="limit">The deployment's limit.</param>
    /// <returns>The message, or null.</returns>
    public static string? Refusal(CorpusStorageEstimate estimate, CorpusStorageLimit limit)
    {
        if (!limit.IsStated || estimate.TotalBytes <= limit.HeadroomBytes)
            return null;

        return $"Server storage: the job would leave about {Bytes(estimate.TotalBytes)} on the server{Window(limit)}, " +
               $"more than the {Bytes(limit.HeadroomBytes)} the deployment allows one job ({limit.Source}). " +
               "Split the manifest into smaller jobs, turn off a product the lab does not need (\"output\": \"stats_and_hashes\" keeps no text and no signatures), or ask the operator to raise the headroom.";
    }

    /// <summary>The preflight's line: the estimate part by part and the limit it is held to.</summary>
    /// <param name="estimate">The job's estimate.</param>
    /// <param name="limit">The deployment's limit.</param>
    /// <returns>The message.</returns>
    public static string Describe(CorpusStorageEstimate estimate, CorpusStorageLimit limit)
    {
        var parts = new List<string>();
        if (estimate.ProductsKept)
            parts.Add($"text {Bytes(estimate.TextBytes)}");
        else
            parts.Add("no text and no signatures (output stats_and_hashes: they stay on the nodes)");
        if (estimate.SignatureBytes > 0)
            parts.Add($"signatures {Bytes(estimate.SignatureBytes)}");
        if (estimate.SampledShards > 0)
            parts.Add($"second copies of {estimate.SampledShards} sampled shard(s) {Bytes(estimate.ReplicaBytes)}");
        parts.Add($"documents {Bytes(estimate.DocumentBytes)}");
        parts.Add($"inputs {Bytes(estimate.InputBytes)}");

        var line = $"Server storage estimate {Bytes(estimate.TotalBytes)}{Window(limit)}: {string.Join(", ", parts)}.";

        if (estimate.UnsizedSources > 0)
            line += $" {estimate.UnsizedSources} source(s) without a known size are not in it.";

        line += limit.IsStated
            ? $" The deployment allows one job {Bytes(limit.HeadroomBytes)} ({limit.Source})."
            : " The deployment states no headroom for one job, so storage refuses nothing.";

        return line;
    }

    /// <summary>A byte count for a person: the exact number and a rounded one.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>For example "571234567 bytes (544.8 MiB)".</returns>
    public static string Bytes(long bytes)
    {
        var rounded = bytes >= BYTES_PER_GIB
            ? (bytes / BYTES_PER_GIB).ToString("F2", CultureInfo.InvariantCulture) + " GiB"
            : (bytes / BYTES_PER_MIB).ToString("F1", CultureInfo.InvariantCulture) + " MiB";

        return $"{bytes.ToString(CultureInfo.InvariantCulture)} bytes ({rounded})";
    }

    private static string Window(CorpusStorageLimit limit)
    {
        return limit.RetentionDays > 0 ? $" for {limit.RetentionDays} day(s) after each shard" : "";
    }

    #endregion
}
