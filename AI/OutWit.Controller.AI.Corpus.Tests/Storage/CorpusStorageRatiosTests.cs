using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Storage;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Storage;

/// <summary>
/// The ratios the storage estimate stands on hold the heads of a real crawl under every switch
/// setting, with the language stage on and off: a stage change that makes products larger fails
/// here and the table moves with it. The table was measured on real crawl data and is held to
/// real crawl data: those cases carry the category <c>Crawl</c> (fetched by range, ignored where
/// the origin cannot be reached). The generated shards are held to what does not depend on what
/// a page says: the layout of the signatures blob. And the table covers every format and setting.
/// </summary>
[TestFixture]
public sealed class CorpusStorageRatiosTests
{
    #region Constants

    private const string WET_FIXTURE = CorpusFixtures.WET;
    private const string WARC_FIXTURE = CorpusFixtures.WARC;
    private const string CRAWL_WET = CorpusFixtures.CRAWL_WET;
    private const string CRAWL_WARC = CorpusFixtures.CRAWL_WARC;
    private const string MODEL_FIXTURE = CorpusFixtures.PACK;
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

    [TestCase(WET_FIXTURE, CorpusSourceFormat.Wet, false)]
    [TestCase(WARC_FIXTURE, CorpusSourceFormat.Warc, false)]
    [TestCase(CRAWL_WET, CorpusSourceFormat.Wet, true, Category = CorpusFixtures.CRAWL)]
    [TestCase(CRAWL_WARC, CorpusSourceFormat.Warc, true, Category = CorpusFixtures.CRAWL)]
    public void TheRatiosHoldTheGoldenShardsTest(string fixture, CorpusSourceFormat format, bool real)
    {
        // The WARC shards hold a PDF and the default pipeline reads it.
        if (format == CorpusSourceFormat.Warc)
            PdfTestModule.Require();

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

                // What a megabyte of crawl makes is a fact of real crawl data: generated pages are not held to it.
                if (real)
                {
                    Assert.That(text / (double)size, Is.LessThanOrEqualTo(row.TextPerByte), $"{setting}: text per byte");
                    Assert.That(documents / (size / BYTES_PER_MIB), Is.LessThanOrEqualTo(row.DocumentsPerMib), $"{setting}: documents per MiB");
                }

                Assert.That(signatures, Is.EqualTo(CorpusStorageRatios.SIGNATURE_HEADER_BYTES + documents * CorpusStorageRatios.SIGNATURE_BYTES_PER_DOCUMENT), $"{setting}: the signatures layout");
            }
        });
    }

    [Test]
    [Category(CorpusFixtures.CRAWL)]
    public void TheEstimateHoldsTheGoldenShardsProductsTest()
    {
        PdfTestModule.Require();
        var sources = new List<CorpusSourceData>
        {
            new() { Url = "https://data.commoncrawl.org/a.warc.wet.gz", Format = CorpusSourceFormat.Wet, SizeBytes = new FileInfo(Fixture(CRAWL_WET)).Length },
            new() { Url = "https://data.commoncrawl.org/a.warc.gz", Format = CorpusSourceFormat.Warc, SizeBytes = new FileInfo(Fixture(CRAWL_WARC)).Length }
        };
        var pipeline = new CorpusPipelineData { Version = "ratio-guard" };

        var estimate = CorpusStorageEstimator.Estimate(sources, pipeline, 0, 0);
        var wet = Run(CRAWL_WET, CorpusSourceFormat.Wet, pipeline);
        var warc = Run(CRAWL_WARC, CorpusSourceFormat.Warc, pipeline);

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
        return CorpusFixtures.PathOf(name);
    }

    #endregion
}
