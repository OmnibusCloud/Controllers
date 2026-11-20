using System.IO.Compression;
using System.Text;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// What one shard may produce. Nothing counted a shard's documents against its size: a shard of
/// a few hundred kilobytes made of one-word pages (one gzip member, headers that repeat) gave
/// 300,000 documents, 392 times its size on the node's disk with 128 hashes and 3,137 times with
/// 1,024, while the disk question, the uploads the preflight stated and the storage refusal were
/// sized for crawl-like density. A shard's product is now held to its documents and its
/// compressed text as a function of the bytes fetched, and past either it is a verdict.
/// </summary>
[TestFixture]
public sealed class ShardProductLimitsTests
{
    #region Constants

    private const long MIB = 1024 * 1024;

    #endregion

    #region Fields

    private string m_directory = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witai_corpus_product_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        CorpusTestFolders.Delete(m_directory);
    }

    #endregion

    #region Bound Tests

    [Test]
    public void TheBoundsAreAFunctionOfTheBytesFetchedTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ShardProductLimits.MaxDocuments(0), Is.EqualTo(4_096));
            Assert.That(ShardProductLimits.MaxDocuments(2 * MIB), Is.EqualTo(4_096), "a small shard has the floor");
            Assert.That(ShardProductLimits.MaxDocuments(3 * MIB), Is.EqualTo(6_144));
            Assert.That(ShardProductLimits.MaxDocuments(3 * MIB + 512), Is.EqualTo(6_145), "one document a 512 bytes");
            Assert.That(ShardProductLimits.MaxDocuments(1L << 30), Is.EqualTo(2_097_152));
            Assert.That(ShardProductLimits.MaxDocuments(-5), Is.EqualTo(4_096));
            Assert.That(ShardProductLimits.MaxTextBytes(0), Is.EqualTo(16 * MIB));
            Assert.That(ShardProductLimits.MaxTextBytes(100 * MIB), Is.EqualTo(200 * MIB));
            Assert.That(ShardProductLimits.MaxTextBytes(long.MaxValue), Is.EqualTo(long.MaxValue), "no wrap");
            Assert.That(ShardProductLimits.MaxProductBytes(100 * MIB, new CorpusPipelineData { Sign = false }), Is.EqualTo(200 * MIB), "no signatures");
            Assert.That(ShardProductLimits.MaxProductBytes(100 * MIB, null), Is.EqualTo(200 * MIB + 12 + 204_800L * 1024), "the text, the header and a signature of 128 values a document");
            Assert.That(ShardProductLimits.MaxUploadBytes(100 * MIB, new CorpusPipelineData { Output = CorpusOutputMode.StatsAndHashes }), Is.Zero, "nothing leaves the node");
        });
    }

    [Test]
    public void NoBoundWrapsWhateverTheBytesTest()
    {
        var wide = Unfiltered(1024);

        Assert.Multiple(() =>
        {
            Assert.That(ShardProductLimits.MaxSignatureBytes(long.MaxValue, OutWit.Controller.AI.Corpus.Signatures.MinHashParameters.Resolve(wide.MinHash)), Is.EqualTo(long.MaxValue));
            Assert.That(ShardProductLimits.MaxProductBytes(long.MaxValue, wide), Is.EqualTo(long.MaxValue));
            Assert.That(ShardProductLimits.MaxProductBytes(long.MaxValue / 4, wide), Is.EqualTo(long.MaxValue), "its signatures at 1,024 hashes are past the largest number");
            Assert.That(ShardProductLimits.MaxUploadBytes(long.MaxValue / 2, wide), Is.EqualTo(long.MaxValue));
            Assert.That(ShardDisk.Needed(long.MaxValue / 4, wide), Is.EqualTo(long.MaxValue));
            Assert.That(ShardDisk.NeededByNode(long.MaxValue / 4, 8, wide), Is.EqualTo(long.MaxValue));
        });
    }

    [TestCase(128)]
    [TestCase(1024)]
    public void AShardOfOneWordPagesStopsAtTheDocumentsItsBytesMayGiveTest(int hashes)
    {
        // The audit's shard, smaller: one gzip member of conversion records whose headers repeat.
        var shard = OneWordPages(50_000);
        var pipeline = Unfiltered(hashes);
        var text = new MemoryStream();
        var signatures = new MemoryStream();

        var exception = Assert.Throws<ShardProductOversizedException>(() => new ShardProcessor(pipeline, null).Process(new MemoryStream(shard), CorpusSourceFormat.Wet, text, signatures));

        var bound = ShardProductLimits.MaxDocuments(shard.Length);
        Assert.Multiple(() =>
        {
            Assert.That(shard.Length, Is.LessThan(2 * MIB), "a shard of the floor's size");
            Assert.That(exception!.What, Is.EqualTo("documents"));
            Assert.That(exception.Limit, Is.EqualTo(bound));
            Assert.That(signatures.Length, Is.LessThanOrEqualTo(16 + (bound + 1) * hashes * 8L), "the signatures stop with the document past the bound");
            Assert.That(text.Length, Is.LessThanOrEqualTo(ShardProductLimits.MaxTextBytes(shard.Length)));
        });
    }

    [Test]
    public void AProductPastItsTextBoundIsOversizedTest()
    {
        // No shard of a test's size passes the text's floor of 16 MiB: the bound is made small.
        var shard = WarcSynth.Shard(Enumerable.Range(0, 40).Select(index => WarcSynth.ConversionRecord($"https://a.example/{index}", WarcSynth.Paragraphs(new Random(index), 300))));
        var processor = new ShardProcessor(Unfiltered(128), null) { TextCeiling = 4096 };

        var exception = Assert.Throws<ShardProductOversizedException>(() => processor.Process(new MemoryStream(shard), CorpusSourceFormat.Wet, new MemoryStream(), new MemoryStream()));

        Assert.That(exception!.What, Is.EqualTo("bytes of compressed text"));
    }

    [Test]
    public void TheHonestShardsOfTheTestsAreInsideTheBoundsTest()
    {
        // The generated fixtures, and the densest synthetic shards the suite makes.
        var shards = new List<(string Name, byte[] Bytes, CorpusSourceFormat Format)>
        {
            (CorpusFixtures.WET, File.ReadAllBytes(CorpusFixtures.PathOf(CorpusFixtures.WET)), CorpusSourceFormat.Wet),
            (CorpusFixtures.WARC, File.ReadAllBytes(CorpusFixtures.PathOf(CorpusFixtures.WARC)), CorpusSourceFormat.Warc),
            ("synthetic 3,000 x 400 words", Synthetic(3_000, 400), CorpusSourceFormat.Wet),
            ("synthetic 6,000 x 300 words", Synthetic(6_000, 300), CorpusSourceFormat.Wet)
        };

        foreach (var (name, bytes, format) in shards)
        {
            var text = new MemoryStream();
            var product = new ShardProcessor(Unfiltered(128), null).Process(new MemoryStream(bytes), format, text, new MemoryStream());
            var documentsPerMib = product.Stats.DocumentsOut * (double)MIB / bytes.Length;
            TestContext.Out.WriteLine($"{name}: {bytes.Length} bytes, {product.Stats.DocumentsOut} documents ({documentsPerMib:F0} a MiB), text {text.Length} bytes ({(double)text.Length / bytes.Length:F2} a byte)");

            Assert.Multiple(() =>
            {
                Assert.That(product.Stats.DocumentsOut, Is.LessThanOrEqualTo(ShardProductLimits.MaxDocuments(bytes.Length)), name);
                Assert.That(text.Length, Is.LessThanOrEqualTo(ShardProductLimits.MaxTextBytes(bytes.Length)), name);
            });
        }
    }

    #endregion

    #region Task Tests

    [Test]
    public async Task ADenseShardIsAVerdictOnEveryNodeAndNothingOfItIsKeptTest()
    {
        var shards = Path.Combine(m_directory, "shards");
        Directory.CreateDirectory(shards);
        await File.WriteAllBytesAsync(Path.Combine(shards, "dense.warc.wet.gz"), OneWordPages(50_000));
        using var origin = new CorpusTestOrigin(shards);
        var blobs = new CorpusTestBlobService(Path.Combine(m_directory, "blobs"));
        var task = new CorpusTaskData
        {
            TaskIndex = 0,
            Source = new CorpusSourceData { Url = origin.ShardUrl("dense.warc.wet.gz"), Format = CorpusSourceFormat.Wet },
            Pipeline = Unfiltered(128),
            AllowedOrigins = [origin.Host],
            MaxShardBytes = 1L << 30,
            Ticket = CorpusTickets.New(),
            RequiredControllerVersion = CorpusTestRun.VERSION
        };

        var result = await ShardTaskRunner.RunAsync(task, new ShardFetcher(ShardFetchPolicy.Immediate, CorpusTestOrigin.Policy), blobs, Path.Combine(m_directory, "work"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.Oversized));
            Assert.That(result.Error, Is.EqualTo(CorpusResultCodes.OVERSIZED_PRODUCT));
            Assert.That(result.TextBlobId, Is.EqualTo(Guid.Empty), "nothing uploaded");
            Assert.That(result.SignaturesBlobId, Is.EqualTo(Guid.Empty));
            Assert.That(result.Stats, Is.Null);
            Assert.That(CorpusResultCheck.IsWellFormed(result), Is.True);
        });
    }

    [Test]
    public void AResultThatClaimsMoreThanItsBytesMayGiveIsNotWellFormedTest()
    {
        // An unsampled shard rests on one machine's word: its counts are held to the same bound.
        var task = CorpusTestRun.Task(0);
        var honest = CorpusTestRun.Completed(task);
        var bytes = honest.Stats!.BytesFetched;

        var bound = (int)ShardProductLimits.MaxDocuments(bytes);
        var atTheBound = CorpusTestRun.Completed(task, documentsOut: bound);
        var documents = CorpusTestRun.Completed(task, documentsOut: bound + 1);

        var text = CorpusTestRun.Completed(task);
        text.Stats!.BytesProduced = ShardProductLimits.MaxTextBytes(bytes) + 1;

        Assert.Multiple(() =>
        {
            Assert.That(CorpusResultCheck.IsWellFormed(honest), Is.True);
            Assert.That(CorpusResultCheck.IsWellFormed(atTheBound), Is.True, "the bound itself is taken");
            Assert.That(CorpusResultCheck.IsWellFormed(documents), Is.False, "more documents than the bytes fetched may give");
            Assert.That(CorpusResultCheck.IsWellFormed(text), Is.False, "more text than the bytes fetched may give");
        });
    }

    #endregion

    #region Tools

    /// <summary>One gzip member of one-word conversion records with the same headers: hundreds of documents a kilobyte.</summary>
    private static byte[] OneWordPages(int count)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            for (var index = 0; index < count; index++)
            {
                var record = Encoding.ASCII.GetBytes($"WARC/1.0\r\nWARC-Type: conversion\r\nWARC-Target-URI: https://a.example/{index}\r\nContent-Length: 4\r\n\r\nword\r\n\r\n");
                gzip.Write(record);
            }
        }

        return output.ToArray();
    }

    private static byte[] Synthetic(int documents, int words)
    {
        return WarcSynth.Shard(Enumerable.Range(0, documents).Select(index => WarcSynth.ConversionRecord($"https://synthetic.example/{index}", WarcSynth.Paragraphs(new Random(index), words))));
    }

    /// <summary>Every stage that drops a document off: the product is every page.</summary>
    private static CorpusPipelineData Unfiltered(int hashes)
    {
        return new CorpusPipelineData
        {
            Version = "product",
            IdentifyLanguage = false,
            CleanLines = false,
            FilterQuality = false,
            ExtractPdf = false,
            Sign = true,
            MinHash = new CorpusMinHashData { Hashes = hashes, ShingleWords = 5, Bands = hashes == 128 ? 16 : 32 }
        };
    }

    #endregion
}
