using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Storage;

namespace OutWit.Controller.AI.Corpus.Tests.Storage;

/// <summary>
/// The ratios the storage estimate stands on hold the golden shards under every switch setting,
/// with the language stage on and off: a stage change that makes products larger fails here
/// and the table moves with it. And the table covers every format and setting.
/// </summary>
[TestFixture]
public sealed class CorpusStorageRatiosTests
{
    #region Constants

    private const string WET_FIXTURE = "cc-main-2026-43-00000-head.warc.wet.gz";
    private const string WARC_FIXTURE = "cc-main-2026-43-00000-head.warc.gz";
    private const string MODEL_FIXTURE = "language-model-cc-2026-43-16.bin";
    private const double BYTES_PER_MIB = 1024.0 * 1024.0;

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

    #region Tests

    [Test]
    public void TheTableCoversEveryFormatAndSettingTest()
    {
        foreach (var format in Enum.GetValues<CorpusSourceFormat>())
        foreach (var cleanLines in new[] { true, false })
        foreach (var filterQuality in new[] { true, false })
        {
            var rows = CorpusStorageRatios.TABLE.Count(row => row.Format == format && row.CleanLines == cleanLines && row.FilterQuality == filterQuality);
            Assert.That(rows, Is.EqualTo(1), $"{format} clean_lines={cleanLines} filter_quality={filterQuality}");
        }
    }

    [TestCase(WET_FIXTURE, CorpusSourceFormat.Wet)]
    [TestCase(WARC_FIXTURE, CorpusSourceFormat.Warc)]
    public void TheRatiosHoldTheGoldenShardsTest(string fixture, CorpusSourceFormat format)
    {
        var size = new FileInfo(Fixture(fixture)).Length;

        Assert.Multiple(() =>
        {
            foreach (var cleanLines in new[] { true, false })
            foreach (var filterQuality in new[] { true, false })
            foreach (var identifyLanguage in new[] { true, false })
            {
                var pipeline = new CorpusPipelineData { Version = "ratio-guard", CleanLines = cleanLines, FilterQuality = filterQuality, IdentifyLanguage = identifyLanguage };
                var (text, signatures, documents) = Run(fixture, format, pipeline);
                var row = CorpusStorageRatios.For(format, pipeline);
                var setting = $"{format} clean_lines={cleanLines} filter_quality={filterQuality} identify_language={identifyLanguage}";

                Assert.That(text / (double)size, Is.LessThanOrEqualTo(row.TextPerByte), $"{setting}: text per byte");
                Assert.That(documents / (size / BYTES_PER_MIB), Is.LessThanOrEqualTo(row.DocumentsPerMib), $"{setting}: documents per MiB");
                Assert.That(signatures, Is.EqualTo(CorpusStorageRatios.SIGNATURE_HEADER_BYTES + documents * CorpusStorageRatios.SIGNATURE_BYTES_PER_DOCUMENT), $"{setting}: the signatures layout");
            }
        });
    }

    [Test]
    public void TheEstimateHoldsTheGoldenShardsProductsTest()
    {
        var sources = new List<CorpusSourceData>
        {
            new() { Url = "https://data.commoncrawl.org/a.warc.wet.gz", Format = CorpusSourceFormat.Wet, SizeBytes = new FileInfo(Fixture(WET_FIXTURE)).Length },
            new() { Url = "https://data.commoncrawl.org/a.warc.gz", Format = CorpusSourceFormat.Warc, SizeBytes = new FileInfo(Fixture(WARC_FIXTURE)).Length }
        };
        var pipeline = new CorpusPipelineData { Version = "ratio-guard" };

        var estimate = CorpusStorageEstimator.Estimate(sources, pipeline, 0, 0);
        var wet = Run(WET_FIXTURE, CorpusSourceFormat.Wet, pipeline);
        var warc = Run(WARC_FIXTURE, CorpusSourceFormat.Warc, pipeline);

        Assert.Multiple(() =>
        {
            Assert.That(estimate.TextBytes, Is.GreaterThanOrEqualTo(wet.Text + warc.Text));
            Assert.That(estimate.SignatureBytes, Is.GreaterThanOrEqualTo(wet.Signatures + warc.Signatures));
        });
    }

    #endregion

    #region Tools

    private (long Text, long Signatures, long Documents) Run(string fixture, CorpusSourceFormat format, CorpusPipelineData pipeline)
    {
        var processor = new ShardProcessor(pipeline, pipeline.IdentifyLanguage ? m_model : null);
        using var shard = File.OpenRead(Fixture(fixture));
        using var text = new MemoryStream();
        using var signatures = new MemoryStream();

        var product = processor.Process(shard, format, text, signatures);
        return (text.Length, signatures.Length, product.Stats.DocumentsOut);
    }

    private static string Fixture(string name)
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    }

    #endregion
}
