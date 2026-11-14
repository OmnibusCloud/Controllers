using System.Security.Cryptography;
using System.Text;
using OutWit.Controller.AI.Corpus.Benchmark;
using OutWit.Controller.AI.Corpus.Pdf;

namespace OutWit.Controller.AI.Corpus.Diagnostics;

/// <summary>
/// The stage set of this controller version: the stages in the order a document meets them,
/// and the fingerprint that tells whether they compute here what they compute everywhere. The
/// fingerprint is the SHA-256 of one short text: the content hash of the benchmark's reference
/// shard through the stages (WET reading, the line stage, the quality filter, the JSON lines of
/// the product), computed here, and the PDF stage's contract — the extractor module's pin and
/// every limit and shaping rule (<see cref="PdfStageLimits.DESCRIPTOR"/>) — so that a changed
/// limit is a changed stage set even where no PDF runs. The stages are built into the controller,
/// so a controller version is a stage set; the fingerprint moves with any stage whose product
/// changes, and the pin with it. The language stage needs a job's model and is not in the
/// fingerprint: its data — the profiles, the acceptance limits and every language's rule data the
/// line stage and the quality filter read (script, stop words, thresholds) — is a language pack in
/// <see cref="LANGUAGE_FORMAT"/>, which the manifest pins by hash; the fingerprint's documents
/// carry no language and are judged by the script defaults, which no pack changes. The MinHash and
/// zstd digests and the PDF stage's own products are pinned by the
/// controller's tests.
/// </summary>
public static class CorpusStageSet
{
    #region Constants

    /// <summary>The stages in the order a document meets them; a job's pipeline switches turn extract_pdf, identify_language, clean_lines, filter_quality and sign on and off.</summary>
    public static readonly IReadOnlyList<string> STAGES =
    [
        "read",
        "extract",
        "extract_pdf",
        "identify_language",
        "clean_lines",
        "filter_quality",
        "compress",
        "sign"
    ];

    /// <summary>The format of the language data the language stage reads (WITLID1 models are still read, with Corpus 0.1.14's rule data).</summary>
    public const string LANGUAGE_FORMAT = Language.LanguageModelFormat.WITLID2;

    /// <summary>The first line of the text the fingerprint hashes; moves when what it covers changes.</summary>
    public const string FINGERPRINT_SCHEMA = "corpus-stage-set/2";

    /// <summary>The pinned fingerprint: <see cref="Compose"/> of the benchmark's reference product and this controller's PDF stage.</summary>
    public const string PINNED_SHA256 = "48be081f37c3a060936f6d5f80df736f3191bcf22936aa1d4da9d83950d8a682";

    #endregion

    #region Functions

    /// <summary>Runs the reference shard through the stages in this process (a few hundred milliseconds of one core) and composes the fingerprint.</summary>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The fingerprint computed here, and whether it is the pinned one.</returns>
    public static (string Sha256, bool Verified) Fingerprint(CancellationToken cancellationToken = default)
    {
        var sha256 = Compose(CorpusBenchmark.ProcessReference(CorpusBenchmarkShard.Bytes, cancellationToken));
        return (sha256, string.Equals(sha256, PINNED_SHA256, StringComparison.Ordinal));
    }

    /// <summary>The fingerprint of a reference product with this controller's PDF stage.</summary>
    /// <param name="referenceSha256">The content hash of the reference shard through the stages.</param>
    /// <returns>Lower-case hex SHA-256.</returns>
    public static string Compose(string referenceSha256)
    {
        var text = $"{FINGERPRINT_SCHEMA}\nreference {referenceSha256}\npdf {PdfStageLimits.DESCRIPTOR}\n";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    #endregion
}
