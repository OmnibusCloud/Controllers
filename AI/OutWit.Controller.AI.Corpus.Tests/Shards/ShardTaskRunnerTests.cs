using OutWit.Controller.AI.Corpus.Compression;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// The node-side task against a loopback origin and a file-backed blob store: a good task
/// completes with its products uploaded and the same content hash a direct run gives; every
/// way a task can go wrong is the verdict the result says, and a refused origin is never
/// even requested.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ShardTaskRunnerTests
{
    #region Constants

    private const string WET_FIXTURE = "cc-main-2026-43-00000-head.warc.wet.gz";
    private const string WARC_FIXTURE = "cc-main-2026-43-00000-head.warc.gz";
    private const string MODEL_FIXTURE = "language-model-cc-2026-43-16.bin";

    #endregion

    #region Fields

    private CorpusTestOrigin m_origin = null!;
    private CorpusTestBlobService m_blobs = null!;
    private ShardFetcher m_fetcher = null!;
    private string m_root = null!;
    private Guid m_modelBlob;
    private string m_modelSha = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public async Task Setup()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witai_corpus_runner_{Guid.NewGuid():N}");
        m_origin = new CorpusTestOrigin(Fixtures());
        m_blobs = new CorpusTestBlobService(Path.Combine(m_root, "blobs"));
        m_fetcher = new ShardFetcher(ShardFetcher.CreateClient(), ShardFetchPolicy.Immediate);
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

    #region Tests

    [TestCase(WET_FIXTURE, CorpusSourceFormat.Wet)]
    [TestCase(WARC_FIXTURE, CorpusSourceFormat.Warc)]
    public async Task GoodTaskCompletesWithTheDirectRunsHashTest(string fixture, CorpusSourceFormat format)
    {
        var task = Task(fixture, format);
        var nodeId = Guid.NewGuid();

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), nodeId, CancellationToken.None);

        var direct = new ShardProcessor(task.Pipeline!, LanguageModel.Load(Path.Combine(Fixtures(), MODEL_FIXTURE)));
        using var shard = File.OpenRead(Path.Combine(Fixtures(), fixture));
        using var text = new MemoryStream();
        var expected = direct.Process(shard, format, text, new MemoryStream());

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.Completed), result.Error);
            Assert.That(result.ContentSha256, Is.EqualTo(expected.ContentSha256));
            Assert.That(result.TextBlobId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(result.SignaturesBlobId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(result.NodeId, Is.EqualTo(nodeId));
            Assert.That(result.NodeName, Is.EqualTo(Environment.MachineName));
            Assert.That(result.ControllerVersion, Is.Not.Empty);
            Assert.That(result.SourceUrl, Is.EqualTo(task.Source!.Url));
            Assert.That(result.Stats!.DocumentsOut, Is.EqualTo(expected.Stats.DocumentsOut));
            Assert.That(result.Stats.BytesFetched, Is.EqualTo(new FileInfo(Path.Combine(Fixtures(), fixture)).Length));
            Assert.That(result.Stats.BytesProduced, Is.EqualTo(text.Length));
            Assert.That(result.Error, Is.Empty);
        });

        var uploaded = await File.ReadAllBytesAsync(await m_blobs.GetLocalPathAsync(result.TextBlobId));
        Assert.That(CorpusCompressor.Decompress(uploaded), Is.EqualTo(CorpusCompressor.Decompress(text.ToArray())));
    }

    [Test]
    public async Task RefusedOriginIsNeverRequestedTest()
    {
        var task = Task(WET_FIXTURE, CorpusSourceFormat.Wet);
        task.AllowedOrigins = ["data.commoncrawl.org"];
        var before = m_origin.Requests;

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), Guid.NewGuid(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.OriginRefused));
            Assert.That(result.Error, Does.Contain("not an allowed origin"));
            Assert.That(m_origin.Requests, Is.EqualTo(before));
            Assert.That(result.Stats, Is.Null);
        });
    }

    [Test]
    public async Task MissingModelIsPipelineUnavailableTest()
    {
        var task = Task(WET_FIXTURE, CorpusSourceFormat.Wet);
        task.Pipeline!.LanguageModelBlobId = Guid.Empty;
        var before = m_origin.Requests;

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), Guid.NewGuid(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.PipelineUnavailable));
            Assert.That(m_origin.Requests, Is.EqualTo(before), "the model is checked before the fetch");
        });
    }

    [Test]
    public async Task ModelOffItsPinIsPipelineUnavailableTest()
    {
        ShardLanguageModels.Clear();
        var task = Task(WET_FIXTURE, CorpusSourceFormat.Wet);
        task.Pipeline!.LanguageModelSha256 = new string('0', 64);

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), Guid.NewGuid(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.PipelineUnavailable));
            Assert.That(result.Error, Does.Contain("does not match the pinned"));
        });
    }

    [Test]
    public async Task CapMakesTheTaskOversizedTest()
    {
        var task = Task(WET_FIXTURE, CorpusSourceFormat.Wet);
        task.MaxShardBytes = 10_000;

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), Guid.NewGuid(), CancellationToken.None);

        Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.Oversized));
    }

    [Test]
    public async Task MissingShardIsFetchFailedTest()
    {
        var task = Task("missing.warc.wet.gz", CorpusSourceFormat.Wet);

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), Guid.NewGuid(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.FetchFailed));
            Assert.That(result.Error, Does.Contain("404"));
        });
    }

    [Test]
    public async Task PublishedHashMismatchIsFetchFailedTest()
    {
        var task = Task(WET_FIXTURE, CorpusSourceFormat.Wet);
        task.Source!.Sha256 = new string('f', 64);

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), Guid.NewGuid(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.FetchFailed));
            Assert.That(result.Error, Does.Contain("not the published"));
        });
    }

    [Test]
    public async Task PublishedHashMatchCompletesTest()
    {
        var task = Task(WET_FIXTURE, CorpusSourceFormat.Wet);
        task.Source!.Sha256 = ShardLanguageModels.Hash(Path.Combine(Fixtures(), WET_FIXTURE));

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), Guid.NewGuid(), CancellationToken.None);

        Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.Completed), result.Error);
    }

    [Test]
    public async Task GarbageIsMalformedTest()
    {
        var task = Task(WET_FIXTURE, CorpusSourceFormat.Wet);
        task.Source!.Url = $"{m_origin.BaseUrl}garbage";

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), Guid.NewGuid(), CancellationToken.None);

        Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.Malformed));
    }

    [Test]
    public async Task LanguageStageOffNeedsNoModelTest()
    {
        var task = Task(WET_FIXTURE, CorpusSourceFormat.Wet);
        task.Pipeline = new CorpusPipelineData { Version = "raw", IdentifyLanguage = false, Sign = false };

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), Guid.NewGuid(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.Completed), result.Error);
            Assert.That(result.SignaturesBlobId, Is.EqualTo(Guid.Empty));
            Assert.That(result.Stats!.Languages.Select(l => l.Name), Is.EqualTo(new[] { "" }));
        });
    }

    [Test]
    public void CancellationPropagatesTest()
    {
        var task = Task(WET_FIXTURE, CorpusSourceFormat.Wet);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.That(() => ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), Guid.NewGuid(), cancelled.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    #endregion

    #region Tools

    private CorpusTaskData Task(string fixture, CorpusSourceFormat format)
    {
        return new CorpusTaskData
        {
            TaskIndex = 3,
            Source = new CorpusSourceData { Url = m_origin.ShardUrl(fixture), Format = format },
            Pipeline = new CorpusPipelineData { Version = "test-v1", LanguageModelBlobId = m_modelBlob, LanguageModelSha256 = m_modelSha },
            AllowedOrigins = [m_origin.Host],
            MaxShardBytes = 1L << 30
        };
    }

    private string Work()
    {
        return Path.Combine(m_root, "work", Guid.NewGuid().ToString("N"));
    }

    private static string Fixtures()
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures");
    }

    #endregion
}
