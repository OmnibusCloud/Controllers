using System.Globalization;
using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Corpus.Compression;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Pdf;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Text;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// The PDF stage inside the pipeline, on a WARC shard of HTML and PDFs of our own making
/// (<see cref="PdfWarcFixture"/>): which responses are PDFs; with the stage off they are what they
/// were (<c>not_html</c>); without a sandbox they drop as <c>pdf_sandbox_unavailable</c> and the
/// HTML's product is untouched; with it each PDF ends in its verdict, a long one becomes
/// page-range documents named <c>url#page=a-b</c>, running headers and footers are gone, the
/// documents go through the language, line and quality stages like a page, and the product and
/// its counters are pinned — the PDF stage's cross-platform proof.
/// </summary>
[TestFixture]
public sealed class ShardPdfStageTests
{
    #region Constants

    private const string MODEL_FIXTURE = "language-model-cc-2026-43-16.bin";

    /// <summary>The product of the fixture through the default pipeline with the stage on (produced on win-x64, re-checked on every platform the workflow runs).</summary>
    private const string GOLDEN_SHA256 = "f0130fa9e18a5b262db1e2ee6f2cdf0dc7db3468f330142e7f56f6e81b3f8930";

    /// <summary>The counters of that run.</summary>
    private const string GOLDEN_STATS = "records=14 extracted=8 out=8 drops=[http_status:1,not_html:2,pdf_empty:1,pdf_encrypted:1,pdf_no_pages:1,pdf_unreadable:1] languages=[eng:8] pdf=[in:10,truncated:1,extracted:6,pages:29,documents:7,cut:1,running:38,fuel:656779560]";

    #endregion

    #region Fields

    private LanguageModel m_model = null!;
    private byte[] m_shard = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_model = LanguageModel.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", MODEL_FIXTURE));
        m_shard = PdfWarcFixture.Shard();
    }

    #endregion

    #region Candidate Tests

    [TestCase("application/pdf", "application/pdf", "%PDF-1.7\n", true)]
    [TestCase("application/pdf; qs=0.001", null, "%PDF-1.4\n", true)]
    [TestCase("APPLICATION/PDF", null, "%PDF-2.0\n", true)]
    [TestCase("application/octet-stream", "application/pdf", "%PDF-1.5\n", true)]
    [TestCase("text/html", "application/pdf", "%PDF-1.5\n", true)]
    [TestCase("application/octet-stream", null, "%PDF-1.5\n", false)]
    [TestCase("application/pdf", "application/pdf", "<html>", false)]
    [TestCase("application/pdf", null, "%PDF", false)]
    public void APdfIsKnownByItsTypeAndItsFirstBytesTest(string contentType, string? identified, string body, bool candidate)
    {
        var record = Record(WarcSynth.PdfResponseRecord("https://h/x", Encoding.Latin1.GetBytes(body), contentType: contentType, identifiedType: identified));
        var payload = HttpResponsePayload.Parse(record.Payload)!;

        Assert.That(ShardPdfStage.IsCandidate(record, payload), Is.EqualTo(candidate));
    }

    #endregion

    #region Stage Switch Tests

    [Test]
    public void WithTheStageOffAPdfIsNotHtmlAsBeforeTest()
    {
        var off = Run(Pipeline(extractPdf: false));
        var stats = off.Product.Stats.Build(0, 0, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(Drops(stats)[ShardProcessor.DROP_NOT_HTML], Is.EqualTo(PdfWarcFixture.PDF_CANDIDATES + 2), "the PDFs, the untyped one and the fake one");
            Assert.That(stats.Drops.Select(drop => drop.Name), Has.None.StartsWith("pdf_"));
            Assert.That(stats.Pdf, Is.Null);
            Assert.That(Ids(off.Text), Is.EqualTo(new[] { PdfWarcFixture.HOST + "page.html" }));
        });
    }

    [Test]
    public void WithoutASandboxThePdfsDropAndTheHtmlGoesOnTest()
    {
        var off = Run(Pipeline(extractPdf: false));
        var unavailable = Run(Pipeline(extractPdf: true), new ShardPdfStage(null, "no sandbox in this test", null));
        var stats = unavailable.Product.Stats.Build(0, 0, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(Drops(stats)[ShardPdfStage.DROP_PDF_SANDBOX_UNAVAILABLE], Is.EqualTo(PdfWarcFixture.PDF_CANDIDATES));
            Assert.That(Drops(stats)[ShardProcessor.DROP_NOT_HTML], Is.EqualTo(2));
            Assert.That(stats.Pdf!.In, Is.EqualTo(PdfWarcFixture.PDF_CANDIDATES));
            Assert.That(stats.Pdf.Truncated, Is.EqualTo(1));
            Assert.That(stats.Pdf.Extracted, Is.Zero);
            Assert.That(unavailable.Product.ContentSha256, Is.EqualTo(off.Product.ContentSha256), "the HTML's product is the same");
        });
    }

    #endregion

    #region Stage Tests

    [Test]
    public void EveryPdfEndsInItsVerdictTest()
    {
        var stats = Run(Pipeline(extractPdf: true), RequireStage()).Product.Stats.Build(0, 0, 0, 0);
        var drops = Drops(stats);

        Assert.Multiple(() =>
        {
            Assert.That(stats.Pdf!.In, Is.EqualTo(PdfWarcFixture.PDF_CANDIDATES));
            Assert.That(stats.Pdf.Truncated, Is.EqualTo(1));
            Assert.That(drops.GetValueOrDefault(ShardPdfStage.DROP_PDF_ENCRYPTED), Is.EqualTo(1), "locked.pdf");
            Assert.That(drops.GetValueOrDefault(ShardPdfStage.DROP_PDF_NO_PAGES), Is.EqualTo(1), "empty-tree.pdf");
            Assert.That(drops.GetValueOrDefault(ShardPdfStage.DROP_PDF_EMPTY), Is.EqualTo(1), "blank.pdf");
            Assert.That(drops.GetValueOrDefault(ShardPdfStage.DROP_PDF_UNREADABLE), Is.EqualTo(1), "broken.pdf");
            Assert.That(drops.GetValueOrDefault(ShardProcessor.DROP_NOT_HTML), Is.EqualTo(2), "untyped.pdf and fake.pdf");
            Assert.That(drops.GetValueOrDefault(ShardProcessor.DROP_HTTP_STATUS), Is.EqualTo(1), "gone.pdf");
            Assert.That(stats.Pdf.Extracted, Is.EqualTo(6), "chronicle, chronicle-cut, open, dense, report, notes");
            Assert.That(stats.Pdf.PagesCut, Is.EqualTo(1), "dense.pdf");
            Assert.That(stats.Pdf.RunningLines, Is.GreaterThanOrEqualTo(24), "the header and the footer of the book's twelve pages at least");
            Assert.That(stats.Pdf.Fuel, Is.GreaterThan(0));
            Assert.That(stats.DocumentsExtracted, Is.EqualTo(1 + stats.Pdf.Documents), "the page and every page-range document");
        });
    }

    [Test]
    public void ALongPdfBecomesPageRangeDocumentsWithoutItsRunningLinesTest()
    {
        var run = Run(Pipeline(extractPdf: true, filterQuality: false, cleanLines: false), RequireStage());
        var documents = Documents(run.Text);
        var chronicle = documents.Where(document => document.Url.StartsWith(PdfWarcFixture.HOST + "chronicle.pdf#", StringComparison.Ordinal)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(chronicle.Select(document => document.Url), Is.EqualTo(new[] { PdfWarcFixture.HOST + "chronicle.pdf#page=1-10", PdfWarcFixture.HOST + "chronicle.pdf#page=11-12" }));
            Assert.That(chronicle.Select(document => PdfTextChunker.Words(document.Text)), Is.All.LessThanOrEqualTo(PdfStageLimits.DOCUMENT_WORDS));
            Assert.That(chronicle.Select(document => document.Text), Has.None.Contain(PdfWarcFixture.HEADER));
            Assert.That(chronicle.Select(document => document.Text), Has.None.Contain(" of 12"));
            Assert.That(documents.Select(document => document.Url), Does.Contain(PdfWarcFixture.HOST + "dense.pdf#page=1-1"));
            Assert.That(documents.Select(document => document.Url), Does.Contain(PdfWarcFixture.HOST + "report.bin#page=1-2"));
            Assert.That(documents.Single(document => document.Url.StartsWith(PdfWarcFixture.HOST + "dense.pdf", StringComparison.Ordinal)).Text, Has.Length.GreaterThan(0));
            Assert.That(PdfTextChunker.Words(documents.Single(document => document.Url.StartsWith(PdfWarcFixture.HOST + "dense.pdf", StringComparison.Ordinal)).Text), Is.LessThanOrEqualTo(PdfStageLimits.DOCUMENT_WORDS));
        });
    }

    [Test]
    public void PdfDocumentsGoThroughTheStagesLikeAPageTest()
    {
        var run = Run(Pipeline(extractPdf: true), RequireStage());
        var stats = run.Product.Stats.Build(0, 0, 0, 0);
        var documents = Documents(run.Text);

        Assert.Multiple(() =>
        {
            Assert.That(documents.Select(document => document.Lang), Is.All.EqualTo("eng"));
            Assert.That(documents.Count(document => document.Url.Contains("#page=", StringComparison.Ordinal)), Is.GreaterThanOrEqualTo(4));
            Assert.That(stats.DocumentsOut, Is.EqualTo(documents.Count));
            Assert.That(ShardSignatureWriter.Read(run.Signatures), Has.Count.EqualTo(documents.Count));
        });
    }

    [Test]
    public void TheSameShardTwiceIsTheSameProductTest()
    {
        var first = Run(Pipeline(extractPdf: true), RequireStage());
        var second = Run(Pipeline(extractPdf: true), RequireStage());

        Assert.Multiple(() =>
        {
            Assert.That(second.Product.ContentSha256, Is.EqualTo(first.Product.ContentSha256));
            Assert.That(second.Product.Stats.Build(0, 0, 0, 0).Is(first.Product.Stats.Build(0, 0, 0, 0)), Is.True);
        });
    }

    [Test]
    public void ThePdfStagesProductIsPinnedTest()
    {
        var run = Run(Pipeline(extractPdf: true), RequireStage());
        var stats = Describe(run.Product.Stats.Build(0, 0, 0, 0));
        TestContext.Progress.WriteLine($"PDF fixture product {run.Product.ContentSha256}: {stats}");

        Assert.Multiple(() =>
        {
            Assert.That(run.Product.ContentSha256, Is.EqualTo(GOLDEN_SHA256));
            Assert.That(stats, Is.EqualTo(GOLDEN_STATS));
        });
    }

    #endregion

    #region Tools

    private ShardPdfStage RequireStage()
    {
        PdfTestModule.Require();
        var stage = new ShardPdfStage(PdfSandboxHost.Instance, PdfSandboxHost.Problem, null);
        Assert.That(PdfSandboxHost.Problem, Is.Null, "the module is here, so the process has a sandbox");
        return stage;
    }

    private (ShardProduct Product, byte[] Text, byte[] Signatures) Run(CorpusPipelineData pipeline, ShardPdfStage? stage = null)
    {
        var processor = new ShardProcessor(pipeline, pipeline.IdentifyLanguage ? m_model : null, stage);
        using var shard = new MemoryStream(m_shard, writable: false);
        using var text = new MemoryStream();
        using var signatures = new MemoryStream();

        var product = processor.Process(shard, CorpusSourceFormat.Warc, text, signatures);
        return (product, text.ToArray(), signatures.ToArray());
    }

    private static CorpusPipelineData Pipeline(bool extractPdf, bool filterQuality = true, bool cleanLines = true)
    {
        return new CorpusPipelineData { Version = "pdf-test-v1", ExtractPdf = extractPdf, FilterQuality = filterQuality, CleanLines = cleanLines };
    }

    private static Dictionary<string, int> Drops(CorpusShardStatsData stats)
    {
        return stats.Drops.ToDictionary(drop => drop.Name, drop => drop.Count, StringComparer.Ordinal);
    }

    private static List<string> Ids(byte[] text)
    {
        return Documents(text).Select(document => document.Url).ToList();
    }

    private static List<(string Url, string Lang, string Text)> Documents(byte[] text)
    {
        return Encoding.UTF8.GetString(CorpusCompressor.Decompress(text))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                return (root.GetProperty("url").GetString()!, root.GetProperty("lang").GetString()!, root.GetProperty("text").GetString()!);
            })
            .ToList();
    }

    /// <summary>The counters as one line: records, documents, drops, languages, the PDF stage's counts.</summary>
    private static string Describe(CorpusShardStatsData stats)
    {
        var pdf = stats.Pdf;
        return string.Create(CultureInfo.InvariantCulture,
            $"records={stats.RecordsIn} extracted={stats.DocumentsExtracted} out={stats.DocumentsOut} " +
            $"drops=[{string.Join(',', stats.Drops.Select(drop => $"{drop.Name}:{drop.Count}"))}] " +
            $"languages=[{string.Join(',', stats.Languages.Select(language => $"{language.Name}:{language.Count}"))}] " +
            $"pdf=[in:{pdf?.In},truncated:{pdf?.Truncated},extracted:{pdf?.Extracted},pages:{pdf?.Pages},documents:{pdf?.Documents},cut:{pdf?.PagesCut},running:{pdf?.RunningLines},fuel:{pdf?.Fuel}]");
    }

    private static WarcRecord Record(byte[] bytes)
    {
        using var reader = new WarcReader(new MemoryStream(bytes));
        return reader.ReadRecords().Single();
    }

    #endregion
}
