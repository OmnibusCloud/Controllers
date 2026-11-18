using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Storage;

/// <summary>
/// The measured ratios the storage estimate is built on: per input format and per setting of
/// the boilerplate-line stage and the quality filter, the compressed text product and the
/// documents per byte of shard fetched. Each value is the largest one measured, raised by
/// <see cref="MARGIN"/> and rounded up (text to the next hundredth, documents to the next ten),
/// so the estimate errs on the side of too much.
/// <para>
/// Measured (2026-11-14, controller 0.1.14). WET with every stage on: the twenty Common Crawl
/// shards of the test fleet's acceptance run (CC-MAIN-2026-43, 1,326,869,161 bytes), run on
/// one machine (the same run under 0.1.13 gave exactly the fleet's products, shard by shard),
/// left 399,862,409 bytes of text (0.3014 of the input; per shard 0.2822 to 0.3160) and
/// 240,165 documents (189.8 per MiB; per shard 180.6 to 196.3), the language stage off a
/// little less; four more full shards of other segments stayed inside both ranges. The two
/// crawl heads are the first 1.5 MB of one WET file and of the WARC file it was converted from
/// (CC-MAIN-2026-43, segment 1791170053823.21, file 00000; the tests fetch them by range and
/// hold them to a hash, they are not in the repository). The WET head reaches 0.3528 of text
/// with the language stage off, inside the row before its margin. Every other row is the two
/// heads with the language stage on and off, the larger of the two. WARC has only its head
/// behind it.
/// </para>
/// <para>
/// Re-measured for 0.2.0 with the PDF stage on (2026-11-14): the WARC head, whose one PDF
/// now gives a document, reaches at most 0.0731 / 0.0861 / 0.0999 / 0.1450 of text per byte and
/// 39.3 / 63.8 / 44.2 / 79.3 documents per MiB (the four WARC rows' settings, the language stage
/// on or off, the larger), each inside its row with the margin; the whole WARC file 00000 of
/// CC-MAIN-2026-43 (923,804,210 bytes, 221 PDFs) gives 0.0196 to 0.0387 and 12.5 to 24.7, a
/// quarter of the head's. The WARC rows stand.
/// </para>
/// <para>
/// A change to a stage that makes products larger shows in the guard test over the two crawl
/// heads (<c>CorpusStorageRatiosTests</c>): the table moves with it.
/// </para>
/// </summary>
public static class CorpusStorageRatios
{
    #region Constants

    /// <summary>What the largest measured value is raised by.</summary>
    public const double MARGIN = 1.2;

    /// <summary>The signatures blob's header with the default MinHash parameters: the magic and the hash count (16 bytes with others: the shingle size follows).</summary>
    public const int SIGNATURE_HEADER_BYTES = 12;

    /// <summary>One document's signature with the default MinHash parameters: 128 values of 8 bytes (a pipeline's hash count times 8 with others).</summary>
    public const int SIGNATURE_BYTES_PER_DOCUMENT = MinHashSigner.HASHES * sizeof(ulong);

    /// <summary>The report's fixed part (schema, summary, histograms).</summary>
    public const int REPORT_BASE_BYTES = 4096;

    /// <summary>One shard's entry in the report (measured 1,956 bytes with a Common Crawl URL, twenty drop rules and sixteen languages).</summary>
    public const int REPORT_BYTES_PER_SHARD = 3072;

    /// <summary>The integrity document's fixed part.</summary>
    public const int INTEGRITY_BASE_BYTES = 1024;

    /// <summary>One sampled pair in the integrity document (measured about 470 bytes).</summary>
    public const int INTEGRITY_BYTES_PER_PAIR = 1024;

    /// <summary>The dedup document's fixed part.</summary>
    public const int DEDUP_BASE_BYTES = 2048;

    /// <summary>One shard's entry in the dedup document, without its keep mask.</summary>
    public const int DEDUP_BYTES_PER_SHARD = 512;

    /// <summary>
    /// One shard's task in a task package (measured 93 bytes: a Common Crawl WARC URL with its
    /// published hash, a ticket and the frame's head, compressed with its neighbours; a guard
    /// test holds packages of 1, 98 and 1,024 such tasks to it).
    /// </summary>
    public const int TASK_PACKAGE_BYTES_PER_SHARD = 128;

    /// <summary>
    /// One shard's result in a result package (measured 568 bytes with every drop rule, sixteen
    /// languages, the unsupported breakdown and the PDF counts, under hashes, blob ids and a
    /// ticket that do not compress; the same guard test holds it).
    /// </summary>
    public const int RESULT_PACKAGE_BYTES_PER_SHARD = 768;

    /// <summary>
    /// A package blob's own bytes: its header and the frame of what its tasks share (measured
    /// about 350 bytes for a pipeline with sixteen target languages; a package of one shard is
    /// 444 bytes of tasks and 1,196 of results).
    /// </summary>
    public const int PACKAGE_BASE_BYTES = 1024;

    /// <summary>
    /// One document in the dedup document: its bit in the keep mask and its share of the
    /// cluster listing (measured 5.6 bytes over 203,462 documents with 11.7 % of them in
    /// clusters; a clustered document costs about 46 bytes, so 16 covers a third clustered).
    /// </summary>
    public const int DEDUP_BYTES_PER_DOCUMENT = 16;

    /// <summary>The table, one row per format and switch setting.</summary>
    public static readonly IReadOnlyList<CorpusStorageRatio> TABLE =
    [
        Row(CorpusSourceFormat.Wet, cleanLines: true, filterQuality: true, textPerByte: 0.38, documentsPerMib: 240),
        Row(CorpusSourceFormat.Wet, cleanLines: true, filterQuality: false, textPerByte: 0.64, documentsPerMib: 350),
        Row(CorpusSourceFormat.Wet, cleanLines: false, filterQuality: true, textPerByte: 0.55, documentsPerMib: 200),
        Row(CorpusSourceFormat.Wet, cleanLines: false, filterQuality: false, textPerByte: 0.98, documentsPerMib: 370),
        Row(CorpusSourceFormat.Warc, cleanLines: true, filterQuality: true, textPerByte: 0.09, documentsPerMib: 50),
        Row(CorpusSourceFormat.Warc, cleanLines: true, filterQuality: false, textPerByte: 0.11, documentsPerMib: 80),
        Row(CorpusSourceFormat.Warc, cleanLines: false, filterQuality: true, textPerByte: 0.12, documentsPerMib: 60),
        Row(CorpusSourceFormat.Warc, cleanLines: false, filterQuality: false, textPerByte: 0.18, documentsPerMib: 100)
    ];

    #endregion

    #region Functions

    /// <summary>
    /// The row for a format under a pipeline. The rows with the quality filter on were measured
    /// with the thresholds the controller is proven with; a run under other thresholds — a
    /// manifest's <c>pipeline.quality</c>, a language pack with rule data of its own — may keep
    /// anything up to what an unfiltered run keeps, so it takes the unfiltered row.
    /// </summary>
    /// <param name="format">The shard's format.</param>
    /// <param name="pipeline">The pipeline (its line stage and quality filter switches, and its threshold overrides).</param>
    /// <param name="measuredThresholds">Whether the language model's rule data is the one the table was measured with (true when the pipeline uses no model).</param>
    /// <returns>The row; an unknown format takes the largest row of all.</returns>
    public static CorpusStorageRatio For(CorpusSourceFormat format, CorpusPipelineData pipeline, bool measuredThresholds = true)
    {
        var filtered = FiltersAsMeasured(pipeline, measuredThresholds);

        return TABLE.FirstOrDefault(row => row.Format == format && row.CleanLines == pipeline.CleanLines && row.FilterQuality == filtered)
               ?? TABLE.OrderByDescending(row => row.TextPerByte).First();
    }

    /// <summary>Whether a pipeline's quality filter runs with the thresholds the table was measured with: the filter is on, the manifest sets no threshold of its own, and the language model's rule data is the measured one.</summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="measuredThresholds">Whether the language model's rule data is the one the table was measured with.</param>
    /// <returns>True when the filtered rows hold for the pipeline.</returns>
    public static bool FiltersAsMeasured(CorpusPipelineData pipeline, bool measuredThresholds = true)
    {
        return pipeline.FilterQuality && measuredThresholds && !OverridesThresholds(pipeline.Quality);
    }

    /// <summary>Whether a manifest's <c>pipeline.quality</c> sets a threshold (the curly-bracket switch only ever drops more, and is no override).</summary>
    /// <param name="quality">The pipeline's thresholds; null when the manifest gives none.</param>
    /// <returns>True when any threshold is set.</returns>
    public static bool OverridesThresholds(CorpusQualityData? quality)
    {
        return quality != null
               && (quality.MinWords > 0 || quality.MaxWords > 0 || quality.MinMeanWordLength > 0 || quality.MaxMeanWordLength > 0
                   || quality.MaxSymbolToWordRatio > 0 || quality.MaxBulletLineRatio > 0 || quality.MaxEllipsisLineRatio > 0
                   || quality.MinAlphabeticWordRatio > 0 || quality.MaxDuplicateLineRatio > 0 || quality.MaxDuplicateLineCharRatio > 0);
    }

    private static CorpusStorageRatio Row(CorpusSourceFormat format, bool cleanLines, bool filterQuality, double textPerByte, double documentsPerMib)
    {
        return new CorpusStorageRatio
        {
            Format = format,
            CleanLines = cleanLines,
            FilterQuality = filterQuality,
            TextPerByte = textPerByte,
            DocumentsPerMib = documentsPerMib
        };
    }

    #endregion
}
