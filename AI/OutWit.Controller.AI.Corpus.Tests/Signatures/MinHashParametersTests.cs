using System.Security.Cryptography;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Signatures;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Signatures;

/// <summary>
/// The MinHash parameters as pipeline knobs: the golden shards' signatures blobs under the
/// default pipeline are pinned to the bytes Corpus 0.2.1 wrote, and the parameters left out,
/// written out at their defaults, or with another band layout, change none of them; the text
/// product does not depend on them; other parameters make a <c>WITSIG2</c> blob that says how it
/// was made, its signatures the first functions of the same family; a value out of its range is
/// refused by name.
/// </summary>
[TestFixture]
public sealed class MinHashParametersTests
{
    #region Constants

    private const string WET_FIXTURE = "cc-main-2026-43-00000-head.warc.wet.gz";
    private const string WARC_FIXTURE = "cc-main-2026-43-00000-head.warc.gz";
    private const string MODEL_FIXTURE = "language-pack-cc-2026-43-16.bin";

    /// <summary>The golden WET shard's signatures blob as Corpus 0.2.1 wrote it (211 documents, 216,076 bytes).</summary>
    private const string GOLDEN_WET_SIGNATURES = "aa77d782313aba066db3054d44013b5c6d4b8b9a7004a90893acb963f93a1add";

    /// <summary>The golden WET shard's text product (the pin of <c>ShardProcessorTests</c>).</summary>
    private const string GOLDEN_WET_PRODUCT = "5fd9af0b09764d3b017cf15c1338b56271faf9f0e857efa653c4dc71a2d3e74b";

    private const string ARTICLE =
        "The committee reviewed the annual report in detail and approved the budget for the coming year. " +
        "Members raised questions about the maintenance schedule, the staffing plan and the reserve fund. " +
        "The chair thanked the volunteers for their work on the summer programme and the autumn fair.";

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

    #region Pin Tests

    /// <summary>The signatures blobs of the golden shards under the default pipeline, as Corpus 0.2.1 wrote them.</summary>
    [TestCase(WET_FIXTURE, CorpusSourceFormat.Wet, GOLDEN_WET_SIGNATURES)]
    [TestCase(WARC_FIXTURE, CorpusSourceFormat.Warc, "e41a88f35141c9840f07f9decfb2f64c6bedf35e31eb09cc7e042d2f8bde998b")]
    public void GoldenSignaturesBlobIsPinnedTest(string fixture, CorpusSourceFormat format, string expected)
    {
        var (_, signatures) = Run(fixture, format, new CorpusPipelineData { Version = "test-v1" });
        var digest = Digest(signatures);

        Assert.That(digest, Is.EqualTo(expected), $"{fixture}: the signatures blob hashes to {digest} ({signatures.Length} bytes)");
    }

    /// <summary>The defaults written out, and another band layout (the merge's business, not the blob's), give the bytes of 0.2.1.</summary>
    [TestCase(128, 5, 16)]
    [TestCase(0, 0, 0)]
    [TestCase(128, 5, 8)]
    [TestCase(0, 0, 32)]
    public void DefaultParametersGiveThePinnedBytesTest(int hashes, int shingleWords, int bands)
    {
        var pipeline = new CorpusPipelineData { Version = "test-v1", MinHash = new CorpusMinHashData { Hashes = hashes, ShingleWords = shingleWords, Bands = bands } };

        var (product, signatures) = Run(WET_FIXTURE, CorpusSourceFormat.Wet, pipeline);

        Assert.Multiple(() =>
        {
            Assert.That(Digest(signatures), Is.EqualTo(GOLDEN_WET_SIGNATURES));
            Assert.That(product.ContentSha256, Is.EqualTo(GOLDEN_WET_PRODUCT));
        });
    }

    [TestCase(64, 5)]
    [TestCase(256, 5)]
    [TestCase(128, 3)]
    [TestCase(1024, 9)]
    public void OtherParametersChangeTheSignaturesAndNotTheTextTest(int hashes, int shingleWords)
    {
        var pipeline = new CorpusPipelineData { Version = "test-v1", MinHash = new CorpusMinHashData { Hashes = hashes, ShingleWords = shingleWords, Bands = 1 } };

        var (product, signatures) = Run(WET_FIXTURE, CorpusSourceFormat.Wet, pipeline);
        var header = ShardSignatureWriter.ReadHeader(signatures);
        var read = ShardSignatureWriter.Read(signatures);

        Assert.Multiple(() =>
        {
            Assert.That(product.ContentSha256, Is.EqualTo(GOLDEN_WET_PRODUCT), "the text product does not depend on the signatures");
            Assert.That(header, Is.EqualTo((hashes, shingleWords, ShardSignatureWriter.HEADER_SIZE_PARAMETERS)));
            Assert.That(signatures.AsSpan(0, 8).ToArray(), Is.EqualTo(ShardSignatureWriter.MAGIC_PARAMETERS));
            Assert.That(read, Has.Count.EqualTo(product.Stats.DocumentsOut));
            Assert.That(read.All(signature => signature.Length == hashes), Is.True);
            Assert.That(signatures.Length, Is.EqualTo(ShardSignatureWriter.HEADER_SIZE_PARAMETERS + product.Stats.DocumentsOut * hashes * 8));
        });
    }

    #endregion

    #region Signer Tests

    [Test]
    public void ASignatureIsThePrefixOfALongerOneTest()
    {
        var standard = MinHashSigner.Sign(ARTICLE);
        var longer = MinHashSigner.Sign(ARTICLE, 512, MinHashSigner.SHINGLE_WORDS);
        var shorter = MinHashSigner.Sign(ARTICLE, 32, MinHashSigner.SHINGLE_WORDS);

        Assert.Multiple(() =>
        {
            Assert.That(longer.Take(MinHashSigner.HASHES), Is.EqualTo(standard), "the family is one sequence: the default's 128 come first");
            Assert.That(shorter, Is.EqualTo(standard.Take(32)));
            Assert.That(MinHashSigner.Sign(ARTICLE, 128, 3), Is.Not.EqualTo(standard), "another shingle size is another set");
        });
    }

    [Test]
    public void TheSignerRefusesParametersOutOfRangeTest()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MinHashSigner.Sign(ARTICLE, 0, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => MinHashSigner.Sign(ARTICLE, MinHashSigner.MAX_HASHES + 1, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => MinHashSigner.Sign(ARTICLE, 128, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => MinHashSigner.Sign(ARTICLE, 128, MinHashSigner.MAX_SHINGLE_WORDS + 1));
            Assert.That(MinHashSigner.Shingles("one two three", 1), Is.EqualTo(new[] { "one", "two", "three" }));
        });
    }

    [Test]
    public void TheHeaderSaysHowTheBlobWasMadeTest()
    {
        using var standard = new MemoryStream();
        ShardSignatureWriter.WriteHeader(standard);
        using var other = new MemoryStream();
        ShardSignatureWriter.WriteHeader(other, new MinHashParameters(256, 7, 32));

        Assert.Multiple(() =>
        {
            Assert.That(ShardSignatureWriter.ReadHeader(standard.ToArray()), Is.EqualTo((128, 5, 12)));
            Assert.That(ShardSignatureWriter.ReadHeader(other.ToArray()), Is.EqualTo((256, 7, 16)));
            Assert.That(ShardSignatureWriter.HeaderSize(MinHashParameters.DEFAULT with { Bands = 4 }), Is.EqualTo(ShardSignatureWriter.HEADER_SIZE), "the bands do not touch the blob");
            Assert.Throws<FormatException>(() => ShardSignatureWriter.ReadHeader("WITSIG9\n0000"u8.ToArray()));
        });
    }

    #endregion

    #region Parameter Tests

    [Test]
    public void ParametersResolveToTheDefaultsTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MinHashParameters.Resolve(null), Is.EqualTo(MinHashParameters.DEFAULT));
            Assert.That(MinHashParameters.Resolve(new CorpusMinHashData()), Is.EqualTo(new MinHashParameters(128, 5, 16)));
            Assert.That(MinHashParameters.Resolve(new CorpusMinHashData { Hashes = 64 }), Is.EqualTo(new MinHashParameters(64, 5, 16)));
            Assert.That(MinHashParameters.DEFAULT.RowsPerBand, Is.EqualTo(8));
            Assert.That(MinHashParameters.DEFAULT.SignatureBytes, Is.EqualTo(1024));
            Assert.That(MinHashParameters.DEFAULT.Describe(), Is.EqualTo("128 hashes over 5-word shingles, 16 bands of 8 rows"));
            Assert.That(MinHashParameters.Problem(null), Is.Null);
            Assert.That(MinHashParameters.Problem(new CorpusMinHashData { Hashes = 1024, ShingleWords = 32, Bands = 1024 }), Is.Null);
        });
    }

    [TestCase(-1, 0, 0, "'hashes' must be an integer from 1 to 1024")]
    [TestCase(1025, 0, 0, "'hashes' must be an integer from 1 to 1024")]
    [TestCase(0, -2, 0, "'shingle_words' must be an integer from 1 to 32")]
    [TestCase(0, 33, 0, "'shingle_words' must be an integer from 1 to 32")]
    [TestCase(0, 0, -1, "'bands' must be an integer from 1 to the hash count, 128")]
    [TestCase(64, 0, 128, "'bands' must be an integer from 1 to the hash count, 64")]
    [TestCase(0, 0, 12, "'bands' (12) must divide 'hashes' (128)")]
    [TestCase(100, 0, 0, "'bands' (16, the default) must divide 'hashes' (100)")]
    [TestCase(8, 0, 0, "'bands' (16, the default) must divide 'hashes' (8)")]
    public void ParametersOutOfRangeAreRefusedByNameTest(int hashes, int shingleWords, int bands, string expected)
    {
        var problem = MinHashParameters.Problem(new CorpusMinHashData { Hashes = hashes, ShingleWords = shingleWords, Bands = bands });

        Assert.That(problem, Does.StartWith(expected));
    }

    [Test]
    public void AProcessorRefusesParametersItCannotUseTest()
    {
        var pipeline = new CorpusPipelineData { Version = "test-v1", MinHash = new CorpusMinHashData { Bands = 12 } };

        Assert.Multiple(() =>
        {
            Assert.That(() => new ShardProcessor(pipeline, m_model), Throws.ArgumentException.With.Message.Contains("'bands' (12) must divide"));
            Assert.That(() => new ShardProcessor(new CorpusPipelineData { Version = "test-v1", Sign = false, MinHash = pipeline.MinHash }, m_model), Throws.Nothing, "a pipeline that does not sign does not use them");
        });
    }

    #endregion

    #region Tools

    private (ShardProduct Product, byte[] Signatures) Run(string fixture, CorpusSourceFormat format, CorpusPipelineData pipeline)
    {
        if (format == CorpusSourceFormat.Warc && pipeline.ExtractPdf)
            PdfTestModule.Require();

        var processor = new ShardProcessor(pipeline, pipeline.IdentifyLanguage ? m_model : null);
        using var shard = File.OpenRead(Fixture(fixture));
        using var text = new MemoryStream();
        using var signatures = new MemoryStream();

        var product = processor.Process(shard, format, text, signatures);
        return (product, signatures.ToArray());
    }

    private static string Digest(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static string Fixture(string name)
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    }

    #endregion
}
