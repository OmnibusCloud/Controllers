using OutWit.Controller.AI.Corpus.Compression;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Pdf;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Trust;
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
    private const string MODEL_FIXTURE = "language-pack-cc-2026-43-16.bin";

    #endregion

    #region Fields

    private CorpusTestOrigin m_origin = null!;
    private readonly List<CorpusTestOrigin> m_servedOrigins = [];
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
        m_fetcher = new ShardFetcher(ShardFetchPolicy.Immediate, CorpusTestOrigin.Policy);
        m_modelBlob = await m_blobs.UploadFileAsync(Path.Combine(Fixtures(), MODEL_FIXTURE));
        m_modelSha = ShardLanguageModels.Hash(Path.Combine(Fixtures(), MODEL_FIXTURE));
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_origin.Dispose();
        m_servedOrigins.ForEach(origin => origin.Dispose());
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

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), CancellationToken.None);

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
            Assert.That(result.NodeId, Is.EqualTo(Guid.Empty), "a result does not say which machine ran it");
            Assert.That(result.NodeName, Is.Empty, "a result does not say which machine ran it");
            Assert.That(result.ControllerVersion, Is.Not.Empty);
            Assert.That(result.SourceUrl, Is.EqualTo(task.Source!.Url));
            Assert.That(result.Stats!.DocumentsOut, Is.EqualTo(expected.Stats.DocumentsOut));
            Assert.That(result.Stats.BytesFetched, Is.EqualTo(new FileInfo(Path.Combine(Fixtures(), fixture)).Length));
            Assert.That(result.Stats.BytesProduced, Is.EqualTo(text.Length));
            Assert.That(result.Stats.WaitMs, Is.GreaterThanOrEqualTo(0));
            Assert.That(result.Stats.FetchMs, Is.GreaterThanOrEqualTo(0));
            Assert.That(result.Error, Is.Empty);
        });

        var uploaded = await File.ReadAllBytesAsync(await m_blobs.GetLocalPathAsync(result.TextBlobId));
        Assert.That(CorpusCompressor.Decompress(uploaded), Is.EqualTo(CorpusCompressor.Decompress(text.ToArray())));
    }

    [Test]
    public async Task ABlobChannelThatDropsForAMomentDoesNotFailTheShardTest()
    {
        // The model download and the first upload each fail once: a server restart, a reconnect.
        var flaky = new CorpusFlakyBlobService(m_blobs, downloadFailures: 1, uploadFailures: 1);
        var task = Task(WET_FIXTURE, CorpusSourceFormat.Wet);

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, flaky, Work(), null, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.Completed), result.Error);
            Assert.That(result.TextBlobId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(result.SignaturesBlobId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(flaky.Downloads, Is.EqualTo(2), "the model: one failure, one success");
            Assert.That(flaky.Uploads, Is.EqualTo(3), "the text twice, the signatures once");
        });
    }

    [Test]
    public void ACancelledJobLeavesTheRetryWaitTest()
    {
        var flaky = new CorpusFlakyBlobService(m_blobs, downloadFailures: int.MaxValue);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        Assert.That(
            async () => await ShardBlobTransfer.DownloadAsync(flaky, Guid.NewGuid(), null, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task HostTheMachineDoesNotFetchFromIsNeverRequestedTest()
    {
        // The task names the loopback origin; this machine's owner did not list it.
        var strict = new ShardFetcher(ShardFetchPolicy.Immediate, new ShardOriginPolicy());
        var task = Task(WET_FIXTURE, CorpusSourceFormat.Wet);
        var before = m_origin.Requests;

        var result = await ShardTaskRunner.RunAsync(task, strict, m_blobs, Work(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.OriginRefused));
            Assert.That(result.Error, Is.EqualTo(CorpusResultCodes.ORIGIN_NOT_LISTED));
            Assert.That(m_origin.Requests, Is.EqualTo(before));
        });
    }

    [Test]
    public async Task RedirectOutOfTheAllowlistIsARefusalNotAFetchFailureTest()
    {
        var task = Task(WET_FIXTURE, CorpusSourceFormat.Wet);
        task.Source!.Url = m_origin.HopUrl("http://169.254.169.254/latest/meta-data/");

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.OriginRefused));
            Assert.That(result.Error, Is.EqualTo(CorpusResultCodes.ORIGIN_REDIRECT));
        });
    }

    [Test]
    public async Task RefusedOriginIsNeverRequestedTest()
    {
        var task = Task(WET_FIXTURE, CorpusSourceFormat.Wet);
        task.AllowedOrigins = ["data.commoncrawl.org"];
        var before = m_origin.Requests;

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.OriginRefused));
            Assert.That(result.Error, Is.EqualTo(CorpusResultCodes.ORIGIN_NOT_ALLOWED));
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

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.PipelineUnavailable));
            Assert.That(result.Error, Is.EqualTo(CorpusResultCodes.NO_LANGUAGE_MODEL));
            Assert.That(m_origin.Requests, Is.EqualTo(before), "the model is checked before the fetch");
        });
    }

    [Test]
    public async Task AModelThatArrivedWithOtherBytesThanItsPinIsANodeFaultTest()
    {
        ShardLanguageModels.Clear();
        var task = Task(WET_FIXTURE, CorpusSourceFormat.Wet);
        task.Pipeline!.LanguageModelSha256 = new string('0', 64);

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            // The server hashed the very blob it pinned: other bytes here are this node's transfer.
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.NodeFault));
            Assert.That(result.Error, Is.EqualTo(CorpusResultCodes.FAULT_LANGUAGE_MODEL));
        });
    }

    [Test]
    public async Task CapMakesTheTaskOversizedTest()
    {
        var task = Task(WET_FIXTURE, CorpusSourceFormat.Wet);
        task.MaxShardBytes = 10_000;

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), CancellationToken.None);

        Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.Oversized));
    }

    [Test]
    public async Task MissingShardIsFetchFailedTest()
    {
        var task = Task("missing.warc.wet.gz", CorpusSourceFormat.Wet);

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.FetchFailed));
            Assert.That(result.Error, Is.EqualTo("http_404"));
        });
    }

    [Test]
    public async Task PublishedHashMismatchIsFetchFailedTest()
    {
        var task = Task(WET_FIXTURE, CorpusSourceFormat.Wet);
        task.Source!.Sha256 = new string('f', 64);

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.FetchFailed));
            Assert.That(result.Error, Is.EqualTo(CorpusResultCodes.HASH_MISMATCH));
        });
    }

    [Test]
    public async Task PublishedHashMatchCompletesTest()
    {
        var task = Task(WET_FIXTURE, CorpusSourceFormat.Wet);
        task.Source!.Sha256 = ShardLanguageModels.Hash(Path.Combine(Fixtures(), WET_FIXTURE));

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), CancellationToken.None);

        Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.Completed), result.Error);
    }

    [Test]
    public async Task GarbageIsMalformedTest()
    {
        var task = Task(WET_FIXTURE, CorpusSourceFormat.Wet);
        task.Source!.Url = $"{m_origin.BaseUrl}garbage";

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), CancellationToken.None);

        Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.Malformed));
    }

    [Test]
    public async Task LanguageStageOffNeedsNoModelTest()
    {
        var task = Task(WET_FIXTURE, CorpusSourceFormat.Wet);
        task.Pipeline = new CorpusPipelineData { Version = "raw", IdentifyLanguage = false, Sign = false };

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.Completed), result.Error);
            Assert.That(result.SignaturesBlobId, Is.EqualTo(Guid.Empty));
            Assert.That(result.Stats!.Languages.Select(l => l.Name), Is.EqualTo(new[] { "" }));
        });
    }

    [Test]
    public async Task APdfRunsInTheTasksOwnScopeTest()
    {
        PdfTestModule.Require();
        var shards = Path.Combine(m_root, "pdf-shards");
        Directory.CreateDirectory(shards);
        await File.WriteAllBytesAsync(Path.Combine(shards, "pdf.warc.gz"), PdfWarcFixture.Shard());
        using var origin = new CorpusTestOrigin(shards);

        var task = Task("pdf.warc.gz", CorpusSourceFormat.Warc);
        task.Source!.Url = origin.ShardUrl("pdf.warc.gz");
        var work = Work();

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, work, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.Completed), result.Error);
            Assert.That(result.Stats!.Pdf!.In, Is.EqualTo(PdfWarcFixture.PDF_CANDIDATES));
            Assert.That(result.Stats.Pdf.Extracted, Is.GreaterThan(0));
            Assert.That(Directory.GetDirectories(work).Select(Path.GetFileName), Is.SupersetOf(new[] { "pdf-in", "pdf-out", "pdf-io" }), "the PDFs' folders are the task's own");
            Assert.That(Directory.EnumerateFiles(Path.Combine(work, "pdf-out"), "*", SearchOption.AllDirectories), Is.Empty, "each run's output is gone after it");
            Assert.That(Directory.EnumerateFiles(Path.Combine(work, "pdf-in")), Is.Empty);
        });
    }

    [Test]
    public async Task ASandboxThatFailsMidShardReturnsTheShardAsANodeFaultTest()
    {
        // Not a verdict (it would be a product, or a failure, of this node alone) and not the end
        // of the node's batch either: the shard goes back unanswered with the fault's code, and
        // the script sends it out once more. (A node that has no sandbox at all refuses its batch
        // before it starts: ShardBatchGuard.)
        var task = await ServedTask("with-pdfs.warc.gz", PdfWarcFixture.Shard());

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), null, null, new ShardPdfStage(null, "no sandbox in this test", null), null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.NodeFault));
            Assert.That(result.Error, Is.EqualTo(CorpusResultCodes.FAULT_PDF_SANDBOX));
            Assert.That(result.Stats, Is.Null);
            Assert.That(result.ContentSha256, Is.Empty);
            Assert.That(result.Ticket, Is.EqualTo(task.Ticket));
        });
    }

    [Test]
    public async Task ANodeWithoutTheSandboxMakesAShardWithoutAPdfTest()
    {
        var bytes = PdfWarcFixture.ShardWithoutPdfs();
        var task = await ServedTask("without-pdfs.warc.gz", bytes);

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), null, null, new ShardPdfStage(null, "no sandbox in this test", null), null, CancellationToken.None);

        var direct = new ShardProcessor(task.Pipeline!, LanguageModel.Load(Path.Combine(Fixtures(), MODEL_FIXTURE)));
        var expected = direct.Process(new MemoryStream(bytes), CorpusSourceFormat.Warc, new MemoryStream(), new MemoryStream());

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.Completed), result.Error);
            Assert.That(result.ContentSha256, Is.EqualTo(expected.ContentSha256), "the product a node with the sandbox makes");
            Assert.That(result.Stats!.Pdf, Is.Null);
        });
    }

    [Test]
    public void CancellationPropagatesTest()
    {
        var task = Task(WET_FIXTURE, CorpusSourceFormat.Wet);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.That(() => ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), cancelled.Token), Throws.InstanceOf<OperationCanceledException>());
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

    /// <summary>A WARC task over bytes served by a loopback origin of their own.</summary>
    private async Task<CorpusTaskData> ServedTask(string name, byte[] bytes)
    {
        var shards = Path.Combine(m_root, "served", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(shards);
        await File.WriteAllBytesAsync(Path.Combine(shards, name), bytes);
        var origin = new CorpusTestOrigin(shards);
        m_servedOrigins.Add(origin);

        var task = Task(name, CorpusSourceFormat.Warc);
        task.Source!.Url = origin.ShardUrl(name);
        return task;
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
