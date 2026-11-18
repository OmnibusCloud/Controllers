using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Common.Logging;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Controller.AI.Corpus.Adapters;
using OutWit.Controller.AI.Corpus.Delivery;
using OutWit.Controller.AI.Corpus.Logging;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Pdf;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Trust;
using OutWit.Controller.AI.Corpus.Variables;
using OutWit.Engine.Data.References;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Logging;

/// <summary>
/// Hardening item C6: a logging call never decides a shard's fate. On clients up to 2.2.5 the
/// host's logger is an in-memory list without a lock; a share of several packages logs from
/// several tasks at once, the list throws, and when it throws inside the engine's own catch block
/// the bare exception leaves the node. Here a node's adapter is driven the way the engine drives
/// it (the adapter base's own entry point), with the packages of a batch started together, into a
/// logger that fails every call, into one that is not thread safe and says so, and into the very
/// logger those clients hand out: every package completes with the product a quiet run gives, and
/// a batch the node refuses still leaves in the refusal's fixed words.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class CorpusLoggerC6Tests
{
    #region Constants

    private const string SHARD = "s.warc.wet.gz";
    private const int PACKAGES = 8;
    private const int SHARDS_PER_PACKAGE = 3;
    private const string PACKAGE_VARIABLE = "package";
    private const string ANSWER_VARIABLE = "answer";

    #endregion

    #region Fields

    private string m_root = null!;
    private CorpusTestOrigin m_origin = null!;
    private CorpusTestBlobService m_blobs = null!;
    private WitTempStorageDefault m_temp = null!;
    private string m_product = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public async Task Setup()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witai_corpus_c6_{Guid.NewGuid():N}");
        var shards = Path.Combine(m_root, "shards");
        Directory.CreateDirectory(shards);
        WarcSynth.WriteWetShard(Path.Combine(shards, SHARD), 40, 200, 5);
        m_origin = new CorpusTestOrigin(shards);
        m_blobs = new CorpusTestBlobService(Path.Combine(m_root, "blobs"));
        m_temp = new WitTempStorageDefault(Path.Combine(m_root, "temp"));
        WitActivityAdapterCorpusProcessPackage.Fetcher = new ShardFetcher(ShardFetchPolicy.Immediate, CorpusTestOrigin.Policy);

        // The product of the shard on a node whose log takes every line.
        var quiet = await RunBatchAsync(NullLogger.Instance, 1);
        m_product = quiet.Single().Results.Select(result => result.ContentSha256).Distinct().Single();
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        WitActivityAdapterCorpusProcessPackage.Fetcher = null;
        m_origin.Dispose();
        ShardCleanup.Drain(TimeSpan.FromMinutes(1));
        CorpusTestFolders.Delete(m_root);
    }

    [SetUp]
    [TearDown]
    public void Forget()
    {
        ShardBatchGuard.Clear();
        PdfSandboxHost.ClearOverride();
    }

    #endregion

    #region Tests

    [Test]
    public async Task C6ALoggerThatFailsEveryCallDecidesNoPackagesFateTest()
    {
        var logger = new CorpusTestLoggerThrowing();
        var lostBefore = CorpusLogger.Lost;

        var outcomes = await RunBatchAsync(logger, PACKAGES);

        Assert.Multiple(() =>
        {
            AssertEveryPackageCompletedWithItsProduct(outcomes);
            Assert.That(logger.Calls, Is.GreaterThanOrEqualTo(PACKAGES), "the node did log: at the least one line for each package");
            Assert.That(CorpusLogger.Lost - lostBefore, Is.GreaterThanOrEqualTo(logger.Calls), "every line was lost, and that is all that was lost");
            Assert.That(outcomes.Select(outcome => outcome.Status.Message ?? ""), Has.None.Contains(CorpusTestLoggerThrowing.MESSAGE));
        });
    }

    [Test]
    public async Task C6ALoggerThatIsNotThreadSafeIsWrittenToOneCallAtATimeTest()
    {
        var logger = new CorpusTestLoggerUnguarded();

        var outcomes = await RunBatchAsync(logger, PACKAGES);

        Assert.Multiple(() =>
        {
            AssertEveryPackageCompletedWithItsProduct(outcomes);
            Assert.That(logger.Overlaps, Is.Zero, "no two of the batch's tasks were inside the host's logger together");
            Assert.That(logger.Lines.Count(line => line.Contains("shard(s) answered", StringComparison.Ordinal)), Is.EqualTo(PACKAGES), "and every package's line is there");
        });
    }

    [Test]
    public async Task C6TheLoggerOfClientsUpTo225TakesABatchOfPackagesTest()
    {
        // The engine's default in-memory logger, the one those clients hand to controllers: an
        // observable list without a lock. Unguarded it fails by chance; guarded it cannot.
        var logger = new SimpleLogger("node");

        var outcomes = await RunBatchAsync(logger, PACKAGES);

        Assert.Multiple(() =>
        {
            AssertEveryPackageCompletedWithItsProduct(outcomes);
            Assert.That(logger.Count(line => line.Contains("shard(s) answered", StringComparison.Ordinal)), Is.EqualTo(PACKAGES), "every package's line is in the host's list");
        });
    }

    [Test]
    public async Task C6ABatchTheNodeRefusesLeavesInItsFixedWordsWhateverTheLoggerDoesTest()
    {
        // A package for another controller version: the node refuses before any work, logs a
        // line and lets the refusal out; the engine's adapter base then logs inside its catch.
        // With a logger that throws there, the logger's exception left the node instead.
        var logger = new CorpusTestLoggerThrowing();
        var adapter = new WitActivityAdapterCorpusProcessPackage(new CorpusTestProcessingManager(), m_blobs, m_temp, logger);
        var package = Package();
        package.RequiredControllerVersion = "0.0.1";

        var outcome = await RunAsync(adapter, Guid.NewGuid(), package);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Status.Result, Is.EqualTo(WitProcessingResult.Failed), "a refused batch fails: the platform moves the work");
            Assert.That(outcome.Status.Message, Does.Contain("one job runs on one controller version"), "the refusal's fixed words");
            Assert.That(outcome.Status.Message, Does.Not.Contain(CorpusTestLoggerThrowing.MESSAGE).And.Not.Contain(nameof(IndexOutOfRangeException)));
            Assert.That(outcome.Answer, Is.Null);
            Assert.That(logger.Calls, Is.GreaterThanOrEqualTo(2), "the adapter's line and the base's own");
        });
    }

    #endregion

    #region Tools

    private void AssertEveryPackageCompletedWithItsProduct(IReadOnlyList<(IWitProcessingStatus Status, CorpusResultPackageData? Answer, List<CorpusResultData> Results)> outcomes)
    {
        Assert.That(outcomes.Select(outcome => outcome.Status.Result), Is.All.EqualTo(WitProcessingResult.Completed), string.Join(" | ", outcomes.Select(outcome => outcome.Status.Message)));
        Assert.That(outcomes.Select(outcome => outcome.Answer?.Fault), Is.All.EqualTo(""), "every package is answered");
        Assert.That(outcomes.Select(outcome => outcome.Results.Count), Is.All.EqualTo(SHARDS_PER_PACKAGE));
        Assert.That(outcomes.SelectMany(outcome => outcome.Results).Select(result => result.Verdict), Is.All.EqualTo(CorpusShardVerdict.Completed));
        Assert.That(outcomes.SelectMany(outcome => outcome.Results).Select(result => result.ContentSha256), Is.All.EqualTo(m_product), "the product of a quiet run");
    }

    /// <summary>One node, one job, a batch of packages started together, as a client starts them.</summary>
    private async Task<List<(IWitProcessingStatus Status, CorpusResultPackageData? Answer, List<CorpusResultData> Results)>> RunBatchAsync(ILogger logger, int packages)
    {
        var adapter = new WitActivityAdapterCorpusProcessPackage(new CorpusTestProcessingManager(), m_blobs, m_temp, logger);
        var jobId = Guid.NewGuid();
        var batch = Enumerable.Range(0, packages).Select(_ => Package()).ToList();
        using var start = new ManualResetEventSlim(false);

        var runs = batch.Select(package => Task.Run(async () =>
        {
            start.Wait();
            return await RunAsync(adapter, jobId, package);
        })).ToList();
        start.Set();

        return (await Task.WhenAll(runs)).ToList();
    }

    private async Task<(IWitProcessingStatus Status, CorpusResultPackageData? Answer, List<CorpusResultData> Results)> RunAsync(WitActivityAdapterCorpusProcessPackage adapter, Guid jobId, CorpusTaskPackageData package)
    {
        var pool = new WitVariableCollection();
        pool.Add(new WitVariableCorpusTaskPackage(PACKAGE_VARIABLE, package));
        pool.Add(new WitVariableCorpusResultPackage(ANSWER_VARIABLE));
        var activity = new WitActivityCorpusProcessPackage { Package = (WitReference)PACKAGE_VARIABLE };
        activity.SetReturnReference(ANSWER_VARIABLE);

        // The engine's own entry point: what a node's engine calls, catch block and all.
        var status = await ((IWitProcessingAdapter)adapter).Process(Guid.NewGuid(), jobId, activity, null, pool, false);

        pool.TryGetValue((WitReference)ANSWER_VARIABLE, out CorpusResultPackageData? answer);
        return (status, answer, answer == null ? [] : CorpusTestPackages.Results(m_blobs, answer));
    }

    private CorpusTaskPackageData Package()
    {
        var tasks = Enumerable.Range(0, SHARDS_PER_PACKAGE).Select(index => new CorpusTaskData
        {
            TaskIndex = index,
            Source = new CorpusSourceData { Url = m_origin.ShardUrl(SHARD), Format = CorpusSourceFormat.Wet },
            Pipeline = new CorpusPipelineData { Version = "c6", IdentifyLanguage = false },
            AllowedOrigins = [m_origin.Host],
            MaxShardBytes = 1L << 30,
            Ticket = CorpusTickets.New(),
            RequiredControllerVersion = CorpusTestRun.VERSION
        }).ToList();

        // One package for all of them, as a job past 1,024 shards has.
        return CorpusTaskPackages.WriteAsync(m_blobs, tasks, tasks.Count * CorpusPackagePlanner.MAX_PACKAGES).GetAwaiter().GetResult().Single();
    }

    #endregion
}
