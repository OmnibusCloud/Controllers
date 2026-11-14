using System.IO.Compression;
using System.Text;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// Hostile input through the whole pipeline: a shard cut mid-record keeps its complete
/// records and says so, invalid UTF-8 and empty payloads are documents that drop rather than
/// faults, hostile HTML is refused by the pre-scan and counted, a record that is not a WARC
/// record at all is a format error at an offset, and nothing a shard contains can change the
/// product of the records before it.
/// </summary>
[TestFixture]
public sealed class ShardProcessorHostileTests
{
    #region Fields

    private LanguageModel m_model = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_model = LanguageModel.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "language-pack-cc-2026-43-16.bin"));
    }

    #endregion

    #region Tests

    [Test]
    public void TruncatedShardKeepsItsCompleteRecordsTest()
    {
        var random = new Random(1);
        var records = Enumerable.Range(0, 5).Select(i => WarcSynth.ConversionRecord($"https://h/{i}", WarcSynth.Paragraphs(random, 200))).ToList();
        var whole = WarcSynth.Shard(records);
        var cut = whole.AsSpan(0, whole.Length - 700).ToArray();

        var full = Run(whole, CorpusSourceFormat.Wet);
        var truncated = Run(cut, CorpusSourceFormat.Wet);

        Assert.Multiple(() =>
        {
            Assert.That(truncated.Product.Truncated, Is.True);
            Assert.That(truncated.Product.Stats.RecordsIn, Is.EqualTo(4));
            Assert.That(full.Product.Stats.RecordsIn, Is.EqualTo(5));
            Assert.That(full.Product.Stats.DocumentsOut, Is.EqualTo(5), "synthetic English paragraphs pass every stage");
            Assert.That(truncated.Product.Stats.DocumentsOut, Is.EqualTo(4));
        });
    }

    [Test]
    public void InvalidUtf8AndEmptyPayloadsDropWithoutFaultTest()
    {
        var random = new Random(2);
        var records = new List<byte[]>
        {
            WarcSynth.Record("conversion", "https://h/garbage", "text/plain", [0xFF, 0xFE, 0xC0, 0xC1, 0x80, 0x80, 0xF8, 0x88, 0x80, 0x80, 0x80]),
            WarcSynth.Record("conversion", "https://h/empty", "text/plain", []),
            WarcSynth.Record("conversion", "https://h/spaces", "text/plain", Encoding.UTF8.GetBytes("   \n\n \t ")),
            WarcSynth.ConversionRecord("https://h/good", WarcSynth.Paragraphs(random, 200))
        };

        var run = Run(WarcSynth.Shard(records), CorpusSourceFormat.Wet);
        var stats = run.Product.Stats.Build(0, 0, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(stats.RecordsIn, Is.EqualTo(4));
            Assert.That(stats.DocumentsOut, Is.EqualTo(1));
            Assert.That(stats.Drops.Single(d => d.Name == ShardProcessor.DROP_EMPTY_TEXT).Count, Is.EqualTo(2));
            Assert.That(stats.DocumentsExtracted, Is.EqualTo(2), "the garbage decodes to replacement characters, a document the later stages drop");
        });
    }

    [Test]
    public void HostileHtmlIsRefusedAndCountedTest()
    {
        var random = new Random(3);
        var deep = new StringBuilder("<html><body>");
        for (var i = 0; i < 2000; i++)
            deep.Append("<div>");
        deep.Append("text");
        var records = new List<byte[]>
        {
            WarcSynth.HtmlResponseRecord("https://h/deep", deep.ToString()),
            WarcSynth.HtmlResponseRecord("https://h/script", "<html><body><script>while(true){}</script><p>" + WarcSynth.Paragraphs(random, 200) + "</p></body></html>"),
            WarcSynth.Record("response", "https://h/binary", "application/http; msgtype=response", Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\n\r\n\0\0\0\0")),
            WarcSynth.Record("response", "https://h/404", "application/http; msgtype=response", Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Type: text/html\r\n\r\n<p>gone</p>")),
            WarcSynth.Record("response", "https://h/nothttp", "application/http; msgtype=response", Encoding.ASCII.GetBytes("this is not an http response at all")),
            WarcSynth.Record("request", "https://h/req", "application/http; msgtype=request", Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n\r\n"))
        };

        var run = Run(WarcSynth.Shard(records), CorpusSourceFormat.Warc);
        var stats = run.Product.Stats.Build(0, 0, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(stats.RecordsIn, Is.EqualTo(6));
            Assert.That(stats.DocumentsOut, Is.EqualTo(1), "the page with the script survives without its script");
            Assert.That(stats.Drops.Single(d => d.Name == ShardProcessor.DROP_HTML_REFUSED).Count, Is.EqualTo(1));
            Assert.That(stats.Drops.Single(d => d.Name == ShardProcessor.DROP_NOT_HTML).Count, Is.EqualTo(1));
            Assert.That(stats.Drops.Single(d => d.Name == ShardProcessor.DROP_HTTP_STATUS).Count, Is.EqualTo(1));
            Assert.That(stats.Drops.Single(d => d.Name == ShardProcessor.DROP_HTTP_UNPARSABLE).Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void NotAWarcRecordIsAFormatErrorTest()
    {
        var random = new Random(4);
        var records = new List<byte[]>
        {
            WarcSynth.ConversionRecord("https://h/good", WarcSynth.Paragraphs(random, 200)),
            Encoding.ASCII.GetBytes("HELLO/1.0\r\nThis: is not a warc record\r\n\r\n")
        };

        Assert.That(() => Run(WarcSynth.Shard(records), CorpusSourceFormat.Wet), Throws.InstanceOf<WarcFormatException>());
    }

    [Test]
    public void CorruptGzipMemberIsMalformedTest()
    {
        var random = new Random(5);
        var shard = WarcSynth.Shard([WarcSynth.ConversionRecord("https://h/good", WarcSynth.Paragraphs(random, 200))]);
        for (var i = 20; i < 60; i++)
            shard[i] ^= 0x5A;

        Assert.That(() => Run(shard, CorpusSourceFormat.Wet), Throws.InstanceOf<InvalidDataException>().Or.InstanceOf<WarcFormatException>());
    }

    [Test]
    public void ALateDefectDoesNotChangeTheEarlierProductTest()
    {
        var random = new Random(6);
        var good = Enumerable.Range(0, 3).Select(i => WarcSynth.ConversionRecord($"https://h/{i}", WarcSynth.Paragraphs(random, 200))).ToList();
        var clean = WarcSynth.Shard(good);
        var dirty = WarcSynth.Shard([.. good, WarcSynth.Record("conversion", "https://h/bad", "text/plain", [0xFF, 0xFF])]);

        var first = Run(clean, CorpusSourceFormat.Wet);
        var second = Run(dirty, CorpusSourceFormat.Wet);

        // The defective record adds a drop; the three good documents' lines, and therefore the
        // hash over them, are identical in both products.
        Assert.That(second.Product.ContentSha256, Is.EqualTo(first.Product.ContentSha256));
    }

    [Test]
    public void OversizedRecordIsSkippedAndCountedTest()
    {
        var random = new Random(7);
        var huge = WarcSynth.ConversionRecord("https://h/huge", new string('x', 3 * 1024 * 1024));
        var shard = WarcSynth.Shard([WarcSynth.ConversionRecord("https://h/a", WarcSynth.Paragraphs(random, 200)), huge, WarcSynth.ConversionRecord("https://h/b", WarcSynth.Paragraphs(random, 200))]);

        using var input = new MemoryStream(shard);
        using var reader = new WarcReader(input, maxRecordBytes: 1024 * 1024, ownsStream: false);
        var records = reader.ReadRecords().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(records, Has.Count.EqualTo(2));
            Assert.That(reader.SkippedOversized, Is.EqualTo(1));
        });
    }

    #endregion

    #region Tools

    private (ShardProduct Product, byte[] Text) Run(byte[] shard, CorpusSourceFormat format)
    {
        var processor = new ShardProcessor(new CorpusPipelineData { Version = "hostile-v1" }, m_model);
        using var input = new MemoryStream(shard);
        using var text = new MemoryStream();
        var product = processor.Process(input, format, text, new MemoryStream());
        return (product, text.ToArray());
    }

    #endregion
}
