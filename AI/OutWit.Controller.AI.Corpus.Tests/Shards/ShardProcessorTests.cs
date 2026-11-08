using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Corpus.Compression;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Quality;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Signatures;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// The pipeline over the golden shards: the product is a function of the shard and the
/// pipeline (same hash twice, on WET and on WARC), its JSON lines decompress to what the
/// hash covers, the signatures line up with the documents, the counters add up, and each
/// stage switch does what it says.
/// </summary>
[TestFixture]
public sealed class ShardProcessorTests
{
    #region Constants

    private const string WET_FIXTURE = "cc-main-2026-43-00000-head.warc.wet.gz";
    private const string WARC_FIXTURE = "cc-main-2026-43-00000-head.warc.gz";
    private const string MODEL_FIXTURE = "language-model-cc-2026-43-16.bin";

    /// <summary>Drops that happen before a document counts as extracted.</summary>
    private static readonly HashSet<string> PRE_EXTRACTION =
    [
        ShardProcessor.DROP_HTTP_UNPARSABLE, ShardProcessor.DROP_HTTP_STATUS, ShardProcessor.DROP_NOT_HTML,
        ShardProcessor.DROP_HTML_REFUSED, ShardProcessor.DROP_EMPTY_TEXT
    ];

    #endregion

    #region Fields

    private LanguageModel m_model = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_model = LanguageModel.Load(Fixture(MODEL_FIXTURE));
    }

    #endregion

    #region Determinism Tests

    [TestCase(WET_FIXTURE, CorpusSourceFormat.Wet)]
    [TestCase(WARC_FIXTURE, CorpusSourceFormat.Warc)]
    public void SameShardSamePipelineGivesSameProductTest(string fixture, CorpusSourceFormat format)
    {
        var pipeline = DefaultPipeline();
        var first = Run(fixture, format, pipeline);
        var second = Run(fixture, format, pipeline);

        Assert.Multiple(() =>
        {
            Assert.That(first.Product.ContentSha256, Is.EqualTo(second.Product.ContentSha256));
            Assert.That(first.Text, Is.EqualTo(second.Text), "the compressed bytes differ");
            Assert.That(first.Signatures, Is.EqualTo(second.Signatures));
            Assert.That(first.Product.Stats.Build(0, 0, 0, 0).Is(second.Product.Stats.Build(0, 0, 0, 0)), Is.True);
            Assert.That(first.Product.Stats.DocumentsOut, Is.GreaterThan(20), "the golden shard should keep a meaningful share");
        });
    }

    [TestCase(WET_FIXTURE, CorpusSourceFormat.Wet)]
    [TestCase(WARC_FIXTURE, CorpusSourceFormat.Warc)]
    public void ContentHashCoversTheDecompressedLinesTest(string fixture, CorpusSourceFormat format)
    {
        var run = Run(fixture, format, DefaultPipeline());
        var lines = CorpusCompressor.Decompress(run.Text);

        Assert.Multiple(() =>
        {
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(lines)), Is.EqualTo(run.Product.ContentSha256));
            Assert.That(Lines(lines), Has.Count.EqualTo(run.Product.Stats.DocumentsOut));
        });
    }

    [Test]
    public void EveryLineIsAnObjectWithUrlLangAndTextTest()
    {
        var run = Run(WET_FIXTURE, CorpusSourceFormat.Wet, DefaultPipeline());

        foreach (var line in Lines(CorpusCompressor.Decompress(run.Text)))
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            Assert.Multiple(() =>
            {
                Assert.That(root.EnumerateObject().Select(p => p.Name), Is.EqualTo(new[] { "url", "lang", "text" }));
                Assert.That(root.GetProperty("url").GetString(), Does.StartWith("http"));
                Assert.That(root.GetProperty("lang").GetString(), Has.Length.EqualTo(3));
                Assert.That(root.GetProperty("text").GetString(), Is.Not.Empty);
            });
        }
    }

    [Test]
    public void SignaturesLineUpWithTheDocumentsTest()
    {
        var run = Run(WET_FIXTURE, CorpusSourceFormat.Wet, DefaultPipeline());
        var signatures = ShardSignatureWriter.Read(run.Signatures);
        var texts = Lines(CorpusCompressor.Decompress(run.Text)).Select(line => JsonDocument.Parse(line).RootElement.GetProperty("text").GetString()!).ToList();

        Assert.That(signatures, Has.Count.EqualTo(texts.Count));
        for (var i = 0; i < texts.Count; i++)
            Assert.That(signatures[i], Is.EqualTo(MinHashSigner.Sign(texts[i])), $"signature {i} is not the document's");
    }

    [TestCase(WET_FIXTURE, CorpusSourceFormat.Wet)]
    [TestCase(WARC_FIXTURE, CorpusSourceFormat.Warc)]
    public void ConcurrentShardsGiveTheSingleRunsProductTest(string fixture, CorpusSourceFormat format)
    {
        // A node runs several shards at once in one process: nothing a stage shares between
        // threads (the HTML parser, the language model, the signer's coefficients) may move a product.
        var single = Run(fixture, format, DefaultPipeline()).Product.ContentSha256;
        var hashes = new string[8];
        var workers = Enumerable.Range(0, hashes.Length).Select(i => new Thread(() => hashes[i] = Run(fixture, format, DefaultPipeline()).Product.ContentSha256)).ToList();
        workers.ForEach(worker => worker.Start());
        workers.ForEach(worker => worker.Join());

        Assert.That(hashes, Is.All.EqualTo(single));
    }

    #endregion

    #region Stage Tests

    [Test]
    public void CountersAddUpTest()
    {
        var run = Run(WET_FIXTURE, CorpusSourceFormat.Wet, DefaultPipeline());
        var stats = run.Product.Stats.Build(0, 0, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(stats.RecordsIn, Is.EqualTo(437), "the golden WET shard has 437 records");
            Assert.That(stats.DocumentsExtracted + stats.Drops.Where(d => PRE_EXTRACTION.Contains(d.Name)).Sum(d => d.Count), Is.LessThanOrEqualTo(stats.RecordsIn));
            Assert.That(stats.DocumentsOut + stats.Drops.Where(d => !PRE_EXTRACTION.Contains(d.Name)).Sum(d => d.Count), Is.EqualTo(stats.DocumentsExtracted), "every extracted document is out or dropped by a later stage");
            Assert.That(stats.Languages.Sum(l => l.Count), Is.EqualTo(stats.DocumentsOut));
            Assert.That(stats.Drops.Select(d => d.Name), Is.Ordered.Using((IComparer<string>)StringComparer.Ordinal));
            Assert.That(stats.Languages.Select(l => l.Name), Is.Ordered.Using((IComparer<string>)StringComparer.Ordinal));
        });
    }

    [Test]
    public void TargetLanguagesKeepOnlyThoseTest()
    {
        var all = Run(WET_FIXTURE, CorpusSourceFormat.Wet, DefaultPipeline());
        var english = Run(WET_FIXTURE, CorpusSourceFormat.Wet, new CorpusPipelineData { Version = "test-v1", TargetLanguages = ["eng"] });

        var languages = english.Product.Stats.Build(0, 0, 0, 0).Languages;
        var englishInAll = all.Product.Stats.Build(0, 0, 0, 0).Languages.Single(l => l.Name == "eng").Count;

        Assert.Multiple(() =>
        {
            Assert.That(languages.Select(l => l.Name), Is.EqualTo(new[] { "eng" }));
            Assert.That(languages[0].Count, Is.EqualTo(englishInAll));
            // The language stage runs before the quality filter, so it also drops the non-English
            // documents that quality would have dropped: at least every non-English survivor.
            Assert.That(english.Product.Stats.Build(0, 0, 0, 0).Drops.Single(d => d.Name == ShardProcessor.DROP_LANGUAGE_NOT_TARGET).Count, Is.GreaterThanOrEqualTo(all.Product.Stats.DocumentsOut - englishInAll));
        });
    }

    [Test]
    public void StagesSwitchOffTest()
    {
        var raw = Run(WET_FIXTURE, CorpusSourceFormat.Wet, new CorpusPipelineData { Version = "test-v1", IdentifyLanguage = false, CleanLines = false, FilterQuality = false, Sign = false }, model: null);
        var stats = raw.Product.Stats.Build(0, 0, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(stats.DocumentsOut, Is.EqualTo(stats.DocumentsExtracted), "nothing filters, everything extracted goes out");
            Assert.That(stats.Languages.Select(l => l.Name), Is.EqualTo(new[] { "" }));
            Assert.That(raw.Signatures, Is.Empty);
            Assert.That(Lines(CorpusCompressor.Decompress(raw.Text)).All(line => line.Contains("\"lang\":\"\"")), Is.True);
        });
    }

    [Test]
    public void LineStageKeepsLinesAndCleansThemTest()
    {
        var raw = Run(WET_FIXTURE, CorpusSourceFormat.Wet, new CorpusPipelineData { Version = "test-v1", IdentifyLanguage = false, CleanLines = false, FilterQuality = false, Sign = false });
        var cleaned = Run(WET_FIXTURE, CorpusSourceFormat.Wet, new CorpusPipelineData { Version = "test-v1", IdentifyLanguage = false, CleanLines = true, FilterQuality = false, Sign = false });

        var rawTexts = Lines(CorpusCompressor.Decompress(raw.Text)).Select(line => JsonDocument.Parse(line).RootElement.GetProperty("text").GetString()!).ToList();
        var cleanTexts = Lines(CorpusCompressor.Decompress(cleaned.Text)).Select(line => JsonDocument.Parse(line).RootElement.GetProperty("text").GetString()!).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(rawTexts.Count(text => text.Contains('\n')), Is.GreaterThan(rawTexts.Count * 9 / 10), "WET documents keep their lines");
            Assert.That(cleaned.Product.Stats.Build(0, 0, 0, 0).Drops.Single(d => d.Name == ShardProcessor.DROP_NO_PROSE_LINES).Count, Is.GreaterThan(0), "pages without a prose line drop");
            Assert.That(cleanTexts.Sum(text => text.Length), Is.LessThan(rawTexts.Sum(text => text.Length) * 3 / 4), "the stage removes a real share of the text");
            Assert.That(cleanTexts.SelectMany(text => text.Split('\n')).All(line => LineCleaner.Keep(line, QualityRules.Spaced)), Is.True, "every line left reads like prose");
        });
    }

    [Test]
    public void QualityOverridesTightenTheFilterTest()
    {
        var loose = Run(WET_FIXTURE, CorpusSourceFormat.Wet, DefaultPipeline());
        var strict = Run(WET_FIXTURE, CorpusSourceFormat.Wet, new CorpusPipelineData { Version = "test-v1", Quality = new CorpusQualityData { MinWords = 500 } });

        Assert.That(strict.Product.Stats.DocumentsOut, Is.LessThan(loose.Product.Stats.DocumentsOut));
        Assert.That(strict.Product.Stats.Build(0, 0, 0, 0).Drops.Single(d => d.Name == "min_words").Count, Is.GreaterThan(loose.Product.Stats.Build(0, 0, 0, 0).Drops.Single(d => d.Name == "min_words").Count));
    }

    [Test]
    public void LanguageStageWithoutModelIsRefusedTest()
    {
        Assert.Throws<ArgumentException>(() => new ShardProcessor(DefaultPipeline(), null));
    }

    [Test]
    public void GarbageIsMalformedTest()
    {
        var processor = new ShardProcessor(DefaultPipeline(), m_model);
        using var shard = new MemoryStream(Encoding.ASCII.GetBytes("not a gzip stream at all"));

        Assert.That(() => processor.Process(shard, CorpusSourceFormat.Wet, new MemoryStream(), new MemoryStream()), Throws.InstanceOf<InvalidDataException>().Or.InstanceOf<WarcFormatException>());
    }

    [Test]
    public void CancellationStopsBetweenRecordsTest()
    {
        var processor = new ShardProcessor(DefaultPipeline(), m_model);
        using var shard = File.OpenRead(Fixture(WET_FIXTURE));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => processor.Process(shard, CorpusSourceFormat.Wet, new MemoryStream(), new MemoryStream(), cancelled.Token));
    }

    #endregion

    #region Tools

    private (ShardProduct Product, byte[] Text, byte[] Signatures) Run(string fixture, CorpusSourceFormat format, CorpusPipelineData pipeline, LanguageModel? model = null)
    {
        var processor = new ShardProcessor(pipeline, pipeline.IdentifyLanguage ? model ?? m_model : null);
        using var shard = File.OpenRead(Fixture(fixture));
        using var text = new MemoryStream();
        using var signatures = new MemoryStream();

        var product = processor.Process(shard, format, text, signatures);
        return (product, text.ToArray(), signatures.ToArray());
    }

    private static CorpusPipelineData DefaultPipeline()
    {
        return new CorpusPipelineData { Version = "test-v1" };
    }

    private static List<string> Lines(byte[] jsonl)
    {
        return Encoding.UTF8.GetString(jsonl).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static string Fixture(string name)
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    }

    #endregion
}
