using OutWit.Controller.AI.Corpus.Pdf;
using OutWit.Controller.AI.Corpus.Text;
using OutWit.Controller.AI.Corpus.Trust;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// The PDF stage of a WARC shard: a response whose Content-Type or <c>WARC-Identified-Payload-Type</c>
/// is <c>application/pdf</c> and whose body starts with <c>%PDF-</c> is run through the sandboxed
/// extractor (<see cref="PdfSandbox"/>), its text shaped (<see cref="PdfTextShaper"/>) and cut into
/// page-range documents (<see cref="PdfTextChunker"/>) with ids <c>url#page=a-b</c>; each document
/// then goes through the stages after extraction like a page of HTML. A PDF that gives nothing
/// drops under a <c>pdf_*</c> rule. Without a sandbox in the process, the shard's first PDF throws
/// <see cref="PdfSandboxException"/>: a shard's product never depends on the node that made it, so a
/// node that cannot run the stage does not make it, and the platform runs the shard elsewhere. The
/// sandbox is asked for only when a PDF comes, so a shard without one runs on any node.
/// </summary>
public sealed class ShardPdfStage
{
    #region Constants

    /// <summary>The media type of a PDF.</summary>
    public const string MEDIA_TYPE = "application/pdf";

    /// <summary>Drop rules of the PDF stage.</summary>
    public const string DROP_PDF_ENCRYPTED = "pdf_encrypted";
    public const string DROP_PDF_UNREADABLE = "pdf_unreadable";
    public const string DROP_PDF_NO_PAGES = "pdf_no_pages";
    public const string DROP_PDF_FUEL = "pdf_fuel";
    public const string DROP_PDF_MEMORY = "pdf_memory";
    public const string DROP_PDF_TRAP = "pdf_trap";
    public const string DROP_PDF_ERROR = "pdf_error";
    public const string DROP_PDF_EMPTY = "pdf_empty";
    public const string DROP_PDF_OVERSIZED = "pdf_oversized";

    /// <summary>A PDF met after the shard's PDFs had burned the fuel one shard may spend on them (<see cref="PdfStageLimits.SHARD_FUEL"/>): not run.</summary>
    public const string DROP_PDF_SHARD_FUEL = "pdf_shard_fuel";

    /// <summary>A PDF met after as many of the shard's PDFs were run as one shard may run (<see cref="PdfStageLimits.SHARD_RUNS"/>): not run.</summary>
    public const string DROP_PDF_SHARD_RUNS = "pdf_shard_runs";

    /// <summary>Every drop rule of the stage, in the order of <see cref="PdfVerdict"/> (then <c>pdf_empty</c>, <c>pdf_shard_fuel</c> and <c>pdf_shard_runs</c>).</summary>
    public static readonly IReadOnlyList<string> DROPS =
    [
        DROP_PDF_ENCRYPTED, DROP_PDF_UNREADABLE, DROP_PDF_NO_PAGES, DROP_PDF_FUEL, DROP_PDF_MEMORY,
        DROP_PDF_TRAP, DROP_PDF_ERROR, DROP_PDF_OVERSIZED, DROP_PDF_EMPTY, DROP_PDF_SHARD_FUEL, DROP_PDF_SHARD_RUNS
    ];

    private const string IDENTIFIED_PAYLOAD_TYPE = "WARC-Identified-Payload-Type";
    private const string TRUNCATED = "WARC-Truncated";
    private const string WORK_PREFIX = "witai_corpus_pdf_";

    private static readonly byte[] PDF_MAGIC = "%PDF-"u8.ToArray();

    #endregion

    #region Fields

    private readonly string? m_workDirectory;
    private readonly bool m_resolveHost;
    private PdfSandbox? m_sandbox;
    private string? m_problem;

    #endregion

    #region Constructors

    /// <summary>A stage on the process-wide sandbox (<see cref="PdfSandboxHost"/>), asked for on the first PDF.</summary>
    /// <param name="workDirectory">The folder the PDFs' files live in while they run (the task's temp scope on a node); null for a fresh folder in the system temp path per PDF (tests, tools).</param>
    public ShardPdfStage(string? workDirectory)
    {
        m_workDirectory = workDirectory;
        m_resolveHost = true;
    }

    /// <summary>A stage on a given sandbox, or on none (a shard with a PDF then fails as a fault of the node).</summary>
    /// <param name="sandbox">The sandbox; null when the process has none.</param>
    /// <param name="problem">Why there is none (ignored when there is one).</param>
    /// <param name="workDirectory">The folder the PDFs' files live in while they run; null for a fresh folder in the system temp path per PDF.</param>
    public ShardPdfStage(PdfSandbox? sandbox, string? problem, string? workDirectory)
    {
        m_sandbox = sandbox;
        m_problem = sandbox == null ? problem ?? "no PDF sandbox" : null;
        m_workDirectory = workDirectory;
    }

    #endregion

    #region Functions

    /// <summary>Whether a response is a PDF the stage takes: a PDF by its Content-Type or by Common Crawl's identification, and a body that starts as a PDF does.</summary>
    /// <param name="record">The WARC response record.</param>
    /// <param name="payload">Its HTTP response.</param>
    /// <returns>True for a PDF.</returns>
    public static bool IsCandidate(WarcRecord record, HttpResponsePayload payload)
    {
        var declared = payload.MediaType == MEDIA_TYPE || MediaType(record.Header(IDENTIFIED_PAYLOAD_TYPE)) == MEDIA_TYPE;
        return declared && payload.Body.Span.StartsWith(PDF_MAGIC);
    }

    /// <summary>The documents of one PDF response, counted into the shard's statistics.</summary>
    /// <param name="record">The WARC response record.</param>
    /// <param name="payload">Its HTTP response (a candidate: <see cref="IsCandidate"/>).</param>
    /// <param name="stats">The shard's counters.</param>
    /// <param name="cancellationToken">Cancels the wait for a sandbox slot.</param>
    /// <returns>The page-range documents (id, text) in page order; none when the PDF dropped.</returns>
    /// <exception cref="PdfSandboxException">A fault of the node, not of the PDF (no sandbox in this process, the wall-clock backstop, a module that cannot run): the shard is not this node's to make.</exception>
    public IReadOnlyList<(string Url, string Text)> Extract(WarcRecord record, HttpResponsePayload payload, ShardStatsBuilder stats, CancellationToken cancellationToken)
    {
        stats.PdfIn(record.Header(TRUNCATED) != null);

        var sandbox = Sandbox()
                      ?? throw new PdfSandboxException(CorpusResultCodes.FAULT_PDF_SANDBOX, $"this node cannot run the PDF stage ({m_problem}); the shard holds a PDF, so another node must make its product");

        // One shard's PDFs have one budget together: a shard of documents that each burn their
        // whole fuel must not cost a node a budget per document without end, and a shard of
        // documents that each cost a run and almost no fuel must not cost it a run without end.
        if (stats.PdfFuelSpent() >= ShardFuel)
            return Drop(stats, DROP_PDF_SHARD_FUEL);

        if (stats.PdfRuns >= ShardRuns)
            return Drop(stats, DROP_PDF_SHARD_RUNS);

        stats.PdfRun();
        var extraction = Run(sandbox, payload.Body.Span, cancellationToken);
        stats.PdfFuel(extraction.Fuel);
        if (extraction.Verdict != PdfVerdict.Text)
            return Drop(stats, DropRule(extraction.Verdict));

        var shaped = PdfTextShaper.Shape(extraction.Text);

        // The module's contract: one form feed between two pages, as many pages as it counted.
        if (shaped.Pages.Count != extraction.Pages)
            return Drop(stats, DROP_PDF_ERROR);

        var (chunks, pagesCut) = PdfTextChunker.Chunk(shaped.Pages);
        if (chunks.Count == 0)
            return Drop(stats, DROP_PDF_EMPTY);

        stats.PdfExtracted(extraction.Pages, chunks.Count, pagesCut, shaped.RunningLinesRemoved);

        var url = record.TargetUri ?? "";
        return chunks.Select(chunk => (chunk.Id(url), chunk.Text)).ToList();
    }

    /// <summary>The drop rule of a verdict that gave no text.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <returns>The rule name.</returns>
    public static string DropRule(PdfVerdict verdict)
    {
        return verdict switch
        {
            PdfVerdict.Encrypted => DROP_PDF_ENCRYPTED,
            PdfVerdict.Unreadable => DROP_PDF_UNREADABLE,
            PdfVerdict.NoPages => DROP_PDF_NO_PAGES,
            PdfVerdict.Fuel => DROP_PDF_FUEL,
            PdfVerdict.Memory => DROP_PDF_MEMORY,
            PdfVerdict.Trap => DROP_PDF_TRAP,
            PdfVerdict.Oversized => DROP_PDF_OVERSIZED,
            _ => DROP_PDF_ERROR
        };
    }

    private PdfExtraction Run(PdfSandbox sandbox, ReadOnlySpan<byte> pdf, CancellationToken cancellationToken)
    {
        if (m_workDirectory != null)
            return sandbox.Extract(pdf, m_workDirectory, cancellationToken);

        // A tool's run, outside a task: a folder of its own in the system's temp folder, taken
        // away behind the run's own folder and, like it, off the run's path (ShardCleanup).
        var directory = Path.Combine(Path.GetTempPath(), WORK_PREFIX + Guid.NewGuid().ToString("N"));
        try
        {
            return sandbox.Extract(pdf, directory, cancellationToken);
        }
        finally
        {
            ShardCleanup.Schedule(directory);
        }
    }

    private PdfSandbox? Sandbox()
    {
        if (m_resolveHost && m_sandbox == null && m_problem == null)
        {
            m_sandbox = PdfSandboxHost.Instance;
            m_problem = PdfSandboxHost.Problem;
        }

        return m_sandbox;
    }

    private static IReadOnlyList<(string Url, string Text)> Drop(ShardStatsBuilder stats, string rule)
    {
        stats.Drop(rule);
        return [];
    }

    private static string MediaType(string? contentType)
    {
        if (contentType == null)
            return "";

        var semicolon = contentType.IndexOf(';');
        return Text.Casing.CorpusCasing.ToLowerAscii((semicolon >= 0 ? contentType[..semicolon] : contentType).Trim());
    }

    #endregion

    #region Properties

    /// <summary>Why the stage has no sandbox; null when it has one or has not asked yet.</summary>
    public string? Problem => m_problem;

    /// <summary>The fuel the PDFs of one shard may burn together (tests of a shard past it take a smaller one).</summary>
    internal long ShardFuel { get; init; } = PdfStageLimits.SHARD_FUEL;

    /// <summary>The PDFs of one shard the stage runs (tests of a shard past it take fewer).</summary>
    internal int ShardRuns { get; init; } = PdfStageLimits.SHARD_RUNS;

    #endregion
}
