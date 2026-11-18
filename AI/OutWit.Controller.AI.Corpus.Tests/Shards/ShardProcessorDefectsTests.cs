using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// The generated crawl with a defect put into it (<see cref="CrawlSynthDefects"/>): a product
/// is a function of the records that arrived whole, whatever else a shard holds. A shard cut
/// inside a record's payload makes exactly the product of the records before the cut; records that are no
/// page (a request, a metadata record, a type nobody heard of) move nothing; a member that is no
/// WARC record ends the shard with an error at its offset, the same error on every run.
/// </summary>
[TestFixture]
public sealed class ShardProcessorDefectsTests
{
    #region Constants

    /// <summary>The generated WET shard's product under the default pipeline (the pin of <c>ShardProcessorTests</c>).</summary>
    private const string WET_PRODUCT = "1a8e1ecb509fa6bb2174de5ef411261952c11d1e51debf68e098fb3eccb5c2cd";

    #endregion

    #region Fields

    private LanguageModel m_model = null!;
    private IReadOnlyList<byte[]> m_records = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_model = LanguageModel.Load(CorpusFixtures.PathOf(CorpusFixtures.PACK));
        m_records = CrawlSynthShards.WetRecords(CorpusFixtures.Folder);
    }

    #endregion

    #region Tests

    [TestCase(40, 0)]
    [TestCase(215, 1)]
    [TestCase(400, 300)]
    public void AShardCutInsideAPayloadMakesTheProductOfItsWholeRecordsTest(int whole, int payloadBytes)
    {
        var cut = Run(CrawlSynthDefects.CutInsideAPayload(m_records, whole, payloadBytes));
        var clean = Run(CrawlSynthShards.Pack(m_records.Take(whole)));

        Assert.Multiple(() =>
        {
            Assert.That(cut.Truncated, Is.True, "the reader says the shard ended early");
            Assert.That(clean.Truncated, Is.False);
            Assert.That(cut.Stats.RecordsIn, Is.EqualTo(whole));
            Assert.That(cut.ContentSha256, Is.EqualTo(clean.ContentSha256), "no half record reaches the product");
            Assert.That(cut.SignaturesSha256, Is.EqualTo(clean.SignaturesSha256));
            Assert.That(cut.ContentSha256, Is.Not.EqualTo(WET_PRODUCT), "and a part of a shard is not the shard");
        });
    }

    [Test]
    public void RecordsThatAreNoPageDoNotMoveTheProductTest()
    {
        var (records, strangers) = CrawlSynthDefects.WithStrangers(m_records, 7);

        var product = Run(CrawlSynthShards.Pack(records));

        Assert.Multiple(() =>
        {
            Assert.That(strangers, Is.GreaterThan(150));
            Assert.That(product.Stats.RecordsIn, Is.EqualTo(m_records.Count + strangers), "every record is counted");
            Assert.That(product.ContentSha256, Is.EqualTo(WET_PRODUCT), "and the product is the shard's own: the committed shard's pin");
        });
    }

    [Test]
    public void AMemberThatIsNoRecordEndsTheShardAtItsOffsetOnEveryRunTest()
    {
        var shard = CrawlSynthShards.Pack(CrawlSynthDefects.WithNoise(m_records, 123));

        var first = Assert.Throws<WarcFormatException>(() => Run(shard));
        var second = Assert.Throws<WarcFormatException>(() => Run(shard));

        Assert.Multiple(() =>
        {
            Assert.That(first!.Message, Does.Contain("offset").And.Contain("WARC version line"));
            Assert.That(second!.Message, Is.EqualTo(first.Message), "the same shard fails the same way: two machines agree on it");
        });
    }

    #endregion

    #region Tools

    private ShardProduct Run(byte[] shard)
    {
        var processor = new ShardProcessor(new CorpusPipelineData { Version = "test-v1" }, m_model);
        using var input = new MemoryStream(shard);
        return processor.Process(input, CorpusSourceFormat.Wet, new MemoryStream(), new MemoryStream());
    }

    #endregion
}
