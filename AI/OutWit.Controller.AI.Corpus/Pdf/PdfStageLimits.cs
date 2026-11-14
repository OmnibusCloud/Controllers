using System.Globalization;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Corpus.Pdf;

/// <summary>
/// The contract of the PDF stage: the extractor module (pinned by its SHA-256), the envelope it
/// runs in, and the rules that shape its text into documents. Every value is part of the product:
/// the same PDF under the same values gives the same documents on every node, and a changed value
/// is a changed pipeline, so <see cref="DESCRIPTOR"/> enters the stage set's fingerprint.
/// </summary>
public static class PdfStageLimits
{
    #region Constants

    /// <summary>
    /// SHA-256 of the extractor module, <c>pdftext.wasm</c>: pdf_oxide 0.3.78 built for
    /// wasm32-wasip1 by Rust 1.99.1 from a pinned lock file (OutWit.Controller.AI.Corpus.Assets,
    /// <c>toolchain/</c>), two independent builds byte-identical. A module with other bytes is not
    /// run.
    /// </summary>
    public const string MODULE_SHA256 = "13abba93015f27dac626fb4342cecf12641cf417246fd0f6639b0b05d450b5f6";

    /// <summary>What the extractor module is.</summary>
    public const string EXTRACTOR = "pdftext: pdf_oxide 0.3.78 built for wasm32-wasip1 by Rust 1.99.1";

    /// <summary>The sandbox the module runs in.</summary>
    public const string ENGINE = "wasmtime, binding " + WasmHostBinding.BUILT_AGAINST + " (the controller's own copy of the AI wasm host)";

    /// <summary>The folder of the module beside the controller (the data asset extracts there).</summary>
    public const string MODULE_FOLDER = "wasm";

    /// <summary>The module's file name.</summary>
    public const string MODULE_FILE = "pdftext.wasm";

    /// <summary>
    /// The fuel one PDF may burn, counted from <c>_start</c> (the budget is given after
    /// instantiation, so it does not depend on the platform). About ten times the 99th percentile
    /// of the complete PDFs of one Common Crawl WARC file, measured with the binding's own counter
    /// (see the controller's README); the worst case of a hostile PDF in CPU time.
    /// </summary>
    public const long FUEL = 80_000_000_000;

    /// <summary>The cap of the module's linear memory: a grow past it is refused, and the module ends by its own hand.</summary>
    public const long MEMORY_BYTES = 1L << 30;

    /// <summary>The largest PDF the stage runs (Common Crawl cuts responses at 5 MiB; the WARC reader's record cap is the same 64 MiB).</summary>
    public const int MAX_INPUT_BYTES = 64 << 20;

    /// <summary>The most text one PDF may give; a PDF with more drops as <c>pdf_oversized</c>.</summary>
    public const int MAX_TEXT_BYTES = 16 << 20;

    /// <summary>The most bytes the module's <c>result.json</c> may hold (it writes about forty).</summary>
    public const int MAX_RESULT_BYTES = 4096;

    /// <summary>
    /// The most words of one page-range document: pages are put together while they fit, a page
    /// longer than this is a document of its own, cut at the last line that fits.
    /// </summary>
    public const int DOCUMENT_WORDS = 5_000;

    /// <summary>
    /// The fewest pages a PDF needs before a line that repeats on half its pages or more counts as
    /// a running header or footer. With five pages and more, "half or more" is three pages or more:
    /// a line on two pages can be a heading and its entry in the contents; on three pages and half
    /// the document it is the page's furniture.
    /// </summary>
    public const int RUNNING_LINE_MIN_PAGES = 5;

    /// <summary>The seed of the module's <c>random_get</c> (the standard library's hash seeds; no output depends on it).</summary>
    public const long RANDOM_SEED = 0;

    /// <summary>
    /// The wall-clock backstop of one run, milliseconds. Fuel binds first on any machine that can
    /// run a node (the budget is under a minute of a slow core); a run that reaches this fails its
    /// shard, which is then run elsewhere — it is never a document's verdict.
    /// </summary>
    public const int WALL_MS = 600_000;

    /// <summary>The marker of a refused allocation on the module's stderr (Rust's allocation-failure message).</summary>
    public const string OUT_OF_MEMORY_MARKER = "memory allocation of";

    /// <summary>
    /// The stage's contract as one line: the module, the envelope, the shaping rules. A part of
    /// the stage set's fingerprint.
    /// </summary>
    public static readonly string DESCRIPTOR = string.Create(CultureInfo.InvariantCulture,
        $"pdftext sha256={MODULE_SHA256}; engine=wasmtime {WasmHostBinding.BUILT_AGAINST}; fuel={FUEL} after instantiation; memory={MEMORY_BYTES}; " +
        $"seed={RANDOM_SEED}; input={MAX_INPUT_BYTES}; text={MAX_TEXT_BYTES}; document_words={DOCUMENT_WORDS} at page boundaries, a longer page cut at a line; " +
        $"running_lines=on {RUNNING_LINE_MIN_PAGES}+ pages, on half the pages or more, digits folded");

    #endregion
}
