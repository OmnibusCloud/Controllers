using OutWit.Controller.AI.Corpus.Benchmark;

namespace OutWit.Controller.AI.Corpus.Diagnostics;

/// <summary>
/// The stage set of this controller version: the stages in the order a document meets them,
/// and the fingerprint that tells whether they compute here what they compute everywhere —
/// the reference shard of the benchmark through the stages (WET reading, the line stage, the
/// quality filter, the JSON lines of the product), whose content hash the controller pins.
/// The stages are built into the controller, so a controller version is a stage set; the
/// fingerprint moves with any stage whose product changes, and the pin with it. The language
/// stage needs a job's model and is not in the fingerprint; the MinHash and zstd digests are
/// pinned by the controller's tests.
/// </summary>
public static class CorpusStageSet
{
    #region Constants

    /// <summary>The stages in the order a document meets them; a job's pipeline switches turn identify_language, clean_lines, filter_quality and sign on and off.</summary>
    public static readonly IReadOnlyList<string> STAGES =
    [
        "read",
        "extract",
        "identify_language",
        "clean_lines",
        "filter_quality",
        "compress",
        "sign"
    ];

    /// <summary>The pinned fingerprint: the benchmark's reference product.</summary>
    public const string PINNED_SHA256 = CorpusBenchmark.EXPECTED_CONTENT_SHA256;

    #endregion

    #region Functions

    /// <summary>Runs the reference shard through the stages in this process (a few hundred milliseconds of one core).</summary>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The fingerprint computed here, and whether it is the pinned one.</returns>
    public static (string Sha256, bool Verified) Fingerprint(CancellationToken cancellationToken = default)
    {
        var sha256 = CorpusBenchmark.ProcessReference(CorpusBenchmarkShard.Bytes, cancellationToken);
        return (sha256, string.Equals(sha256, PINNED_SHA256, StringComparison.Ordinal));
    }

    #endregion
}
