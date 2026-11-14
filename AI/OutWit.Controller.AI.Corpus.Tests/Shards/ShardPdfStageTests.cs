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
/// were (<c>not_html</c>); without a sandbox a shard with a PDF is not made at all (a fault of the
/// node) while a shard without one is made as anywhere; with it each PDF ends in its verdict, a long
/// one becomes page-range documents named <c>url#page=a-b</c>, running headers and footers are gone,
/// Cyrillic and Greek words broken at a line end are whole again, the documents go through the
/// language, line and quality stages like a page, and the product and its counters are pinned — the
/// PDF stage's cross-platform proof — under the 0.1.x model (the stage's own pin) and under the
/// reference language pack (where the two meet).
/// </summary>
[TestFixture]
public sealed class ShardPdfStageTests
{
    #region Constants

    private const string MODEL_FIXTURE = "language-model-cc-2026-43-16.bin";
    private const string PACK_FIXTURE = "language-pack-cc-2026-43-16.bin";

    /// <summary>The product of the fixture through the default pipeline with the stage on (produced on win-x64, re-checked on every platform the workflow runs).</summary>
    private const string GOLDEN_SHA256 = "e907a225c20a98aad47b2d321deb68fcbb62df434b7f69097cd76137470bb123";

    /// <summary>The counters of that run.</summary>
    private const string GOLDEN_STATS = "records=16 extracted=10 out=9 drops=[http_status:1,not_html:2,pdf_empty:1,pdf_encrypted:1,pdf_no_pages:1,pdf_unreadable:1,stop_words:1] languages=[eng:8,rus:1] unsupported=[] pdf=[in:12,truncated:1,extracted:8,pages:32,documents:9,cut:1,running:38,fuel:705671179]";

    /// <summary>
    /// The product of the fixture through the same pipeline under the reference language pack: the
    /// same documents (the Greek PDF, the only one the pack judges otherwise, leaves either way), so
    /// the same hash; the counters name it unsupported where the 0.1.x model dropped it by stop words.
    /// </summary>
    private const string PACK_GOLDEN_SHA256 = GOLDEN_SHA256;

    /// <summary>The counters of that run.</summary>
    private const string PACK_GOLDEN_STATS = "records=16 extracted=10 out=9 drops=[http_status:1,language_unsupported:1,not_html:2,pdf_empty:1,pdf_encrypted:1,pdf_no_pages:1,pdf_unreadable:1] languages=[eng:8,rus:1] unsupported=[ell:1] pdf=[in:12,truncated:1,extracted:8,pages:32,documents:9,cut:1,running:38,fuel:705671179]";

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
            Assert.That(stats.Drops.Select(drop => drop.Name), Has.None.EqualTo("pdf_sandbox_unavailable"));
            Assert.That(stats.Drops.Select(drop => drop.Name), Has.None.StartsWith("pdf_"));
            Assert.That(stats.Pdf, Is.Null);
            Assert.That(Ids(off.Text), Is.EqualTo(new[] { PdfWarcFixture.HOST + "page.html" }));
        });
    }

    [Test]
    public void WithoutASandboxAShardWithAPdfIsNotMadeTest()
    {
        // The node that cannot run the stage makes no product of the shard: a fault of the node,
        // never a document's verdict, so no two honest nodes can make different products of it.
        var processor = new ShardProcessor(Pipeline(extractPdf: true), m_model, new ShardPdfStage(null, "no sandbox in this test", null));
        using var shard = new MemoryStream(m_shard, writable: false);

        var exception = Assert.Throws<PdfSandboxException>(() => processor.Process(shard, CorpusSourceFormat.Warc, new MemoryStream(), new MemoryStream()));

        Assert.That(exception!.Message, Does.Contain("no sandbox in this test").And.Contain("another node"));
    }

    [Test]
    public void WithoutASandboxAShardWithoutAPdfIsMadeAsAnywhereTest()
    {
        // No candidate, no sandbox asked for: the node makes the product a node with a sandbox makes.
        var withoutPdfs = PdfWarcFixture.ShardWithoutPdfs();
        var unavailable = Run(Pipeline(extractPdf: true), new ShardPdfStage(null, "no sandbox in this test", null), withoutPdfs);
        var processHost = Run(Pipeline(extractPdf: true), null, withoutPdfs);
        var stats = unavailable.Product.Stats.Build(0, 0, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(unavailable.Product.ContentSha256, Is.EqualTo(processHost.Product.ContentSha256));
            Assert.That(stats.Is(processHost.Product.Stats.Build(0, 0, 0, 0)), Is.True);
            Assert.That(stats.Pdf, Is.Null, "nothing there for the stage");
            Assert.That(stats.DocumentsOut, Is.EqualTo(1), "the page of HTML");
        });
    }

    [Test]
    public void WithoutASandboxAWetShardIsMadeAsAnywhereTest()
    {
        var processor = new ShardProcessor(new CorpusPipelineData { Version = "pdf-test-v1" }, m_model, new ShardPdfStage(null, "no sandbox in this test", null));
        using var shard = File.OpenRead(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "cc-main-2026-43-00000-head.warc.wet.gz"));

        var product = processor.Process(shard, CorpusSourceFormat.Wet, new MemoryStream(), new MemoryStream());

        Assert.That(product.ContentSha256, Is.EqualTo("61ac69cf399a57c75a127b86dc85c02534fc51de4e0e82a896b3de647f735620"), "the golden WET product");
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
            Assert.That(stats.Pdf.Extracted, Is.EqualTo(8), "chronicle, chronicle-cut, open, dense, report, notes, letopis, chroniko");
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
            Assert.That(documents.Select(document => document.Lang).Distinct(), Is.EquivalentTo(new[] { "eng", "rus" }), "the Greek PDF does not pass: the model knows no Greek");
            Assert.That(documents.Select(document => document.Url), Has.Some.StartsWith(PdfWarcFixture.HOST + "letopis.pdf#page="));
            Assert.That(documents.Count(document => document.Url.Contains("#page=", StringComparison.Ordinal)), Is.GreaterThanOrEqualTo(4));
            Assert.That(stats.DocumentsOut, Is.EqualTo(documents.Count));
            Assert.That(ShardSignatureWriter.Read(run.Signatures), Has.Count.EqualTo(documents.Count));
        });
    }

    [TestCase("letopis.pdf", TestName = "CyrillicWordsBrokenAtALineEndAreWholeTest")]
    [TestCase("chroniko.pdf", TestName = "GreekWordsBrokenAtALineEndAreWholeTest")]
    public void WordsBrokenAtALineEndAreWholeTest(string name)
    {
        // The module leaves a non-Latin word broken at a line end as two lines; the stage joins them.
        var broken = name == "letopis.pdf" ? PdfWarcFixture.Russian().Broken : PdfWarcFixture.Greek().Broken;
        var run = Run(new CorpusPipelineData { Version = "pdf-test-v1", ExtractPdf = true, IdentifyLanguage = false, CleanLines = false, FilterQuality = false }, RequireStage());
        var text = string.Join('\n', Documents(run.Text).Where(document => document.Url.StartsWith(PdfWarcFixture.HOST + name + "#", StringComparison.Ordinal)).Select(document => document.Text));
        var words = text.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(word => word.TrimEnd('.')).ToHashSet(StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.Not.Empty);
            Assert.That(broken, Is.Not.Empty);
            Assert.That(broken.Where(word => !words.Contains(word)), Is.Empty, "every broken word is whole");
            Assert.That(text.Split('\n').Where(line => line.EndsWith('-')), Is.Empty, "no line ends in a hyphen");
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

    [Test]
    public void ThePdfStagesProductUnderTheLanguagePackIsPinnedTest()
    {
        // Where the PDF stage meets the language pack: the same PDFs make the same documents, and
        // the ones the pack does not support (the Greek PDF) leave as language_unsupported, named.
        var pack = LanguageModel.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", PACK_FIXTURE));
        var run = Run(Pipeline(extractPdf: true), RequireStage(), model: pack);
        var stats = run.Product.Stats.Build(0, 0, 0, 0);
        var described = Describe(stats);
        TestContext.Progress.WriteLine($"PDF fixture product under the pack {run.Product.ContentSha256}: {described}");

        Assert.Multiple(() =>
        {
            Assert.That(pack.Format, Is.EqualTo(LanguageModelFormat.WITLID2));
            Assert.That(run.Product.ContentSha256, Is.EqualTo(PACK_GOLDEN_SHA256));
            Assert.That(described, Is.EqualTo(PACK_GOLDEN_STATS));
            Assert.That(stats.UnsupportedLanguages.Select(language => language.Name), Does.Contain("ell"), "the Greek PDF, named");
            Assert.That(Documents(run.Text).Select(document => document.Url), Has.None.StartsWith(PdfWarcFixture.HOST + "chroniko.pdf#"));
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

    private (ShardProduct Product, byte[] Text, byte[] Signatures) Run(CorpusPipelineData pipeline, ShardPdfStage? stage = null, byte[]? shardBytes = null, LanguageModel? model = null)
    {
        var processor = new ShardProcessor(pipeline, pipeline.IdentifyLanguage ? model ?? m_model : null, stage);
        using var shard = new MemoryStream(shardBytes ?? m_shard, writable: false);
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

    /// <summary>The counters as one line: records, documents, drops, languages, the unsupported ones, the PDF stage's counts.</summary>
    private static string Describe(CorpusShardStatsData stats)
    {
        var pdf = stats.Pdf;
        return string.Create(CultureInfo.InvariantCulture,
            $"records={stats.RecordsIn} extracted={stats.DocumentsExtracted} out={stats.DocumentsOut} " +
            $"drops=[{string.Join(',', stats.Drops.Select(drop => $"{drop.Name}:{drop.Count}"))}] " +
            $"languages=[{string.Join(',', stats.Languages.Select(language => $"{language.Name}:{language.Count}"))}] " +
            $"unsupported=[{string.Join(',', stats.UnsupportedLanguages.Select(language => $"{language.Name}:{language.Count}"))}] " +
            $"pdf=[in:{pdf?.In},truncated:{pdf?.Truncated},extracted:{pdf?.Extracted},pages:{pdf?.Pages},documents:{pdf?.Documents},cut:{pdf?.PagesCut},running:{pdf?.RunningLines},fuel:{pdf?.Fuel}]");
    }

    private static WarcRecord Record(byte[] bytes)
    {
        using var reader = new WarcReader(new MemoryStream(bytes));
        return reader.ReadRecords().Single();
    }

    #endregion
}
