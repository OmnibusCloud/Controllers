using System.Security.Cryptography;
using System.Text;
using OutWit.Controller.AI.Corpus.Benchmark;
using OutWit.Controller.AI.Corpus.Pdf;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Text;
using OutWit.Controller.AI.Corpus.Text.Casing;

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

    /// <summary>The first line of the text the fingerprint hashes; moves when what it covers changes (<c>/3</c>: the case table and the reading limits).</summary>
    public const string FINGERPRINT_SCHEMA = "corpus-stage-set/3";

    /// <summary>
    /// The case handling of the stages as one line: the Unicode version and the hash of the
    /// lower-case table the language n-grams and the MinHash tokens are made with. A part of the
    /// fingerprint: another table is another product for the documents that hold a code point
    /// whose mapping moved.
    /// </summary>
    public static readonly string CASING = $"unicode {CorpusCasing.UNICODE_VERSION} simple lower case (U+0130 kept), table sha256={CorpusCasing.TABLE_SHA256}; ASCII comparisons fold A-Z only";

    /// <summary>
    /// The limits a shard is read within, as one line: each decides what becomes a document and
    /// what drops, so each is a part of the fingerprint.
    /// </summary>
    public static readonly string READING = string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"record wet<={ShardLimits.MAX_WET_RECORD_BYTES} warc<={ShardLimits.MAX_WARC_RECORD_BYTES} (larger: {ShardProcessor.DROP_RECORD_OVERSIZED}); " +
        $"unpacked<={ShardLimits.UNPACK_RATIO}x within {ShardLimits.MIN_UNPACKED_BYTES}..{ShardLimits.MAX_UNPACKED_BYTES}; " +
        $"html chars<={HtmlTextExtractor.MAX_HTML_CHARS} decoded bytes<={HttpResponsePayload.MAX_DECODE_BYTES} tags<={HtmlTextExtractor.MAX_TAGS} nesting<{HtmlTextExtractor.MAX_NESTING} elements<={HtmlTextExtractor.MAX_ELEMENTS} " +
        $"(pre-scan: {ShardProcessor.DROP_HTML_REFUSED}; parser: {ShardProcessor.DROP_HTML_TOO_COMPLEX}; a failure of the parser's own: {ShardProcessor.DROP_HTML_ERROR}); charset utf-7: {ShardProcessor.DROP_UNSUPPORTED_CHARSET}");

    /// <summary>The pinned fingerprint: <see cref="Compose"/> of the benchmark's reference product and this controller's PDF stage.</summary>
    public const string PINNED_SHA256 = "2f691cec7accdc309207b9859a3193636b2809119020d846fb6c30b0af994678";

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
        var text = $"{FINGERPRINT_SCHEMA}\nreference {referenceSha256}\npdf {PdfStageLimits.DESCRIPTOR}\ncasing {CASING}\nreading {READING}\n";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    #endregion
}
