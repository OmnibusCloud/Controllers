using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Signatures;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// The output mode "statistics and hashes only" on a node: the shard is processed, signed and hashed
/// as under the full output (the same content hash, the same signatures hash, the same counters, the
/// same product size), but nothing is uploaded — no text, no signatures — and the task's folder, with
/// the products in it, is the caller's to delete. The full output uploads both. Either way the result
/// carries the MinHash parameters and the signatures' hash, which integrity compares.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ShardOutputModeTests
{
    #region Constants

    private const string WET_FIXTURE = "cc-main-2026-43-00000-head.warc.wet.gz";
    private const string MODEL_FIXTURE = "language-pack-cc-2026-43-16.bin";

    #endregion

    #region Fields

    private CorpusTestOrigin m_origin = null!;
    private CorpusTestBlobService m_blobs = null!;
    private ShardFetcher m_fetcher = null!;
    private string m_root = null!;
    private string m_blobRoot = null!;
    private Guid m_modelBlob;
    private string m_modelSha = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public async Task Setup()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witai_corpus_output_{Guid.NewGuid():N}");
        m_blobRoot = Path.Combine(m_root, "blobs");
        m_origin = new CorpusTestOrigin(Fixtures());
        m_blobs = new CorpusTestBlobService(m_blobRoot);
        m_fetcher = new ShardFetcher(ShardFetchPolicy.Immediate, CorpusTestOrigin.Policy);
        m_modelBlob = await m_blobs.UploadFileAsync(Path.Combine(Fixtures(), MODEL_FIXTURE));
        m_modelSha = ShardLanguageModels.Hash(Path.Combine(Fixtures(), MODEL_FIXTURE));
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_origin.Dispose();
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    #endregion

    #region Node Tests

    [Test]
    public async Task StatsAndHashesUploadsNothingAndHashesAsTheFullOutputTest()
    {
        var full = await RunCountedAsync(Task(CorpusOutputMode.Full));
        var stats = await RunCountedAsync(Task(CorpusOutputMode.StatsAndHashes));

        Assert.Multiple(() =>
        {
            Assert.That(full.Result.Verdict, Is.EqualTo(CorpusShardVerdict.Completed), full.Result.Error);
            Assert.That(stats.Result.Verdict, Is.EqualTo(CorpusShardVerdict.Completed), stats.Result.Error);
            Assert.That(full.Uploaded, Is.EqualTo(2), "the full output uploads the text and the signatures");
            Assert.That(stats.Uploaded, Is.Zero, "statistics and hashes only uploads nothing");
            Assert.That(stats.Result.TextBlobId, Is.EqualTo(Guid.Empty));
            Assert.That(stats.Result.SignaturesBlobId, Is.EqualTo(Guid.Empty));
            Assert.That(stats.Result.ContentSha256, Is.EqualTo(full.Result.ContentSha256), "the content hash does not depend on what is kept");
            Assert.That(SameCounts(stats.Result.Stats!, full.Result.Stats!), Is.True, "the same counters");
            Assert.That(stats.Result.Stats!.BytesProduced, Is.EqualTo(full.Result.Stats!.BytesProduced), "the product's size on the node");
            Assert.That(stats.Result.Output, Is.EqualTo(CorpusOutputMode.StatsAndHashes));
            Assert.That(full.Result.Output, Is.EqualTo(CorpusOutputMode.Full));
            Assert.That(stats.Result.MinHash!.Is(MinHashParameters.DEFAULT.ToData()), Is.True, "the signatures are made, so their parameters are said");
            Assert.That(full.Result.MinHash!.Is(MinHashParameters.DEFAULT.ToData()), Is.True, "the full output says what it signed with, resolved");
            Assert.That(full.Result.SignaturesSha256, Has.Length.EqualTo(64));
            Assert.That(stats.Result.SignaturesSha256, Is.EqualTo(full.Result.SignaturesSha256), "the signatures' identity does not depend on what is kept");
        });
    }

    [Test]
    public async Task AFullRunWithoutSigningCarriesNoParametersTest()
    {
        var task = Task(CorpusOutputMode.Full);
        task.Pipeline!.Sign = false;
        task.Pipeline.MinHash = new CorpusMinHashData { Hashes = 64 };

        var run = await RunCountedAsync(task);

        Assert.Multiple(() =>
        {
            Assert.That(run.Result.Verdict, Is.EqualTo(CorpusShardVerdict.Completed), run.Result.Error);
            Assert.That(run.Uploaded, Is.EqualTo(1), "the text only");
            Assert.That(run.Result.SignaturesBlobId, Is.EqualTo(Guid.Empty));
            Assert.That(run.Result.MinHash, Is.Null);
            Assert.That(run.Result.SignaturesSha256, Is.Empty, "no signature made, nothing to compare");
        });
    }

    [Test]
    public async Task OtherParametersTravelResolvedInTheResultAndTheBlobTest()
    {
        var task = Task(CorpusOutputMode.Full);
        task.Pipeline!.MinHash = new CorpusMinHashData { Hashes = 64, Bands = 8 };

        var run = await RunCountedAsync(task);
        var blob = await File.ReadAllBytesAsync(await m_blobs.GetLocalPathAsync(run.Result.SignaturesBlobId));

        Assert.Multiple(() =>
        {
            Assert.That(run.Result.Verdict, Is.EqualTo(CorpusShardVerdict.Completed), run.Result.Error);
            Assert.That(run.Result.MinHash!.Is(new CorpusMinHashData { Hashes = 64, ShingleWords = 5, Bands = 8 }), Is.True, $"{run.Result.MinHash}");
            Assert.That(ShardSignatureWriter.ReadHeader(blob), Is.EqualTo((64, 5, ShardSignatureWriter.HEADER_SIZE_PARAMETERS)));
            Assert.That(ShardSignatureWriter.Read(blob), Has.Count.EqualTo(run.Result.Stats!.DocumentsOut));
            Assert.That(run.Result.SignaturesSha256, Is.EqualTo(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(blob))), "the hash is the uploaded blob's");
        });
    }

    #endregion

    #region Processor Tests

    [Test]
    public void TheProcessorSignsAndHashesWhenNothingIsKeptTest()
    {
        var model = LanguageModel.Load(Path.Combine(Fixtures(), MODEL_FIXTURE));
        var pipeline = new CorpusPipelineData { Version = "test-v1", Output = CorpusOutputMode.StatsAndHashes };

        using var shard = File.OpenRead(Path.Combine(Fixtures(), WET_FIXTURE));
        using var text = new MemoryStream();
        using var signatures = new MemoryStream();
        var product = new ShardProcessor(pipeline, model).Process(shard, CorpusSourceFormat.Wet, text, signatures);

        Assert.Multiple(() =>
        {
            Assert.That(ShardProcessor.Signs(pipeline), Is.True, "made, to be hashed and compared");
            Assert.That(ShardProcessor.KeepsSignatures(pipeline), Is.False);
            Assert.That(product.SignaturesSha256, Is.EqualTo(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(signatures.ToArray()))));
            Assert.That(product.SignaturesSha256, Is.EqualTo("aa77d782313aba066db3054d44013b5c6d4b8b9a7004a90893acb963f93a1add"), "the golden WET shard's signatures blob, kept or not");
            Assert.That(text.Length, Is.GreaterThan(0), "the text is still made on the node, to be hashed and measured");
            Assert.That(product.ContentSha256, Is.EqualTo("5fd9af0b09764d3b017cf15c1338b56271faf9f0e857efa653c4dc71a2d3e74b"), "the golden WET product, kept or not");
        });
    }

    #endregion

    #region Tools

    private async Task<(CorpusResultData Result, int Uploaded)> RunCountedAsync(CorpusTaskData task)
    {
        var before = Directory.GetFiles(m_blobRoot).Length;
        var work = Path.Combine(m_root, "work", Guid.NewGuid().ToString("N"));
        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, work, CancellationToken.None);
        return (result, Directory.GetFiles(m_blobRoot).Length - before);
    }

    private CorpusTaskData Task(CorpusOutputMode output)
    {
        return new CorpusTaskData
        {
            TaskIndex = 0,
            Source = new CorpusSourceData { Url = m_origin.ShardUrl(WET_FIXTURE), Format = CorpusSourceFormat.Wet },
            Pipeline = new CorpusPipelineData { Version = "test-v1", LanguageModelBlobId = m_modelBlob, LanguageModelSha256 = m_modelSha, Output = output },
            AllowedOrigins = [m_origin.Host],
            MaxShardBytes = 1L << 30
        };
    }

    private static bool SameCounts(CorpusShardStatsData first, CorpusShardStatsData second)
    {
        return first.RecordsIn == second.RecordsIn
               && first.DocumentsExtracted == second.DocumentsExtracted
               && first.DocumentsOut == second.DocumentsOut
               && first.Drops.Select(drop => (drop.Name, drop.Count)).SequenceEqual(second.Drops.Select(drop => (drop.Name, drop.Count)))
               && first.Languages.Select(language => (language.Name, language.Count)).SequenceEqual(second.Languages.Select(language => (language.Name, language.Count)))
               && first.UnsupportedLanguages.Select(language => (language.Name, language.Count)).SequenceEqual(second.UnsupportedLanguages.Select(language => (language.Name, language.Count)));
    }

    private static string Fixtures()
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures");
    }

    #endregion
}
