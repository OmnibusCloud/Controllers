using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// Audit finding H-8 on the node: the size a shard is declared with is a claim the node enforces.
/// The audit's manifest listed files of a gigabyte with <c>"size": 1</c>; the contributors were
/// promised a kilobyte and fetched a terabyte, because a node held a shard to the 1 GiB cap only.
/// Now a shard far above its declared size is refused by name — a verdict about the input, the
/// same on every node, and no fault of the node's own — whether the origin states its length or
/// streams the body; and the server drops a result that says it fetched more.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ShardTaskRunnerH8Tests
{
    #region Constants

    private const string SHARD = "s.warc.wet.gz";

    #endregion

    #region Fields

    private string m_root = null!;
    private CorpusTestOrigin m_origin = null!;
    private CorpusTestBlobService m_blobs = null!;
    private ShardFetcher m_fetcher = null!;
    private long m_size;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witai_corpus_h8_{Guid.NewGuid():N}");
        var shards = Path.Combine(m_root, "shards");
        Directory.CreateDirectory(shards);

        // Incompressible words: the shard is a few megabytes on the wire, well past any slack.
        WarcSynth.WriteWetShard(Path.Combine(shards, SHARD), 3000, 400, 8);
        m_size = new FileInfo(Path.Combine(shards, SHARD)).Length;
        m_origin = new CorpusTestOrigin(shards);
        m_blobs = new CorpusTestBlobService(Path.Combine(m_root, "blobs"));
        m_fetcher = new ShardFetcher(ShardFetchPolicy.Immediate, CorpusTestOrigin.Policy);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_origin.Dispose();
        CorpusTestFolders.Delete(m_root);
    }

    #endregion

    #region Tests

    [Test]
    public async Task H8AShardFarAboveItsDeclaredSizeIsRefusedByNameTest()
    {
        // The audit's manifest: "size": 1 for a file that is not.
        var task = Task(m_origin.ShardUrl(SHARD), declared: 1);

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(m_size, Is.GreaterThan(2 * ShardLimits.DeclaredCap(1)), "the shard is far above what its declaration allows");
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.Oversized), "a verdict about the input, not a fault of the node");
            Assert.That(result.Error, Is.EqualTo(CorpusResultCodes.OVERSIZED_DECLARED), "by name");
            Assert.That(result.Stats, Is.Null);
            Assert.That(result.TextBlobId, Is.EqualTo(Guid.Empty));
            Assert.That(CorpusResultCheck.IsWellFormed(result), Is.True);
        });
    }

    [Test]
    public async Task H8AStreamedBodyIsCutWhereItsDeclaredSizeEndsTest()
    {
        // An origin that states no length cannot be refused before the body: the body is counted
        // as it arrives and the fetch ends a buffer past the bound, not at the cap.
        var body = await File.ReadAllBytesAsync(Path.Combine(m_root, "shards", SHARD));
        long sent = 0;
        using var origin = new CorpusRawOrigin(async (_, _, stream, cancellationToken) =>
        {
            await stream.WriteAsync(CorpusRawOrigin.Head("200 OK", "Connection: close"), cancellationToken);
            for (var offset = 0; offset < body.Length; offset += 65536)
            {
                var count = Math.Min(65536, body.Length - offset);
                await stream.WriteAsync(body.AsMemory(offset, count), cancellationToken);
                await stream.FlushAsync(cancellationToken);
                Interlocked.Add(ref sent, count);
            }
        });
        var declared = 64 * 1024;
        var task = Task(origin.Url(SHARD), declared);
        var work = Work();

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, work, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.Oversized));
            Assert.That(result.Error, Is.EqualTo(CorpusResultCodes.OVERSIZED_DECLARED));
            Assert.That(File.Exists(Path.Combine(work, "shard.gz")), Is.False, "what arrived is not kept");
        });
    }

    [Test]
    public async Task H8AShardWithinTheSlackOfItsDeclaredSizeIsProcessedTest()
    {
        // A size in a manifest is often an estimate: a mebibyte or an eighth more is no lie.
        var exact = await ShardTaskRunner.RunAsync(Task(m_origin.ShardUrl(SHARD), m_size), m_fetcher, m_blobs, Work(), CancellationToken.None);
        var under = await ShardTaskRunner.RunAsync(Task(m_origin.ShardUrl(SHARD), m_size - 512 * 1024), m_fetcher, m_blobs, Work(), CancellationToken.None);
        var over = await ShardTaskRunner.RunAsync(Task(m_origin.ShardUrl(SHARD), m_size * 4), m_fetcher, m_blobs, Work(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(exact.Verdict, Is.EqualTo(CorpusShardVerdict.Completed));
            Assert.That(under.Verdict, Is.EqualTo(CorpusShardVerdict.Completed), "half a mebibyte above the declaration is inside the slack");
            Assert.That(over.Verdict, Is.EqualTo(CorpusShardVerdict.Completed), "a shard smaller than declared costs the contributors less than promised");
            Assert.That(under.ContentSha256, Is.EqualTo(exact.ContentSha256), "the declaration changes what is refused, never the product");
            Assert.That(over.ContentSha256, Is.EqualTo(exact.ContentSha256));
            Assert.That(exact.Stats!.BytesFetched, Is.EqualTo(m_size));
        });
    }

    [Test]
    public async Task H8AShardWithNoSizeOfItsOwnIsHeldToTheCapTest()
    {
        // No declaration, a cap below the shard: the cap's own word, as before.
        var task = Task(m_origin.ShardUrl(SHARD), declared: 0);
        task.MaxShardBytes = 100_000;

        var capped = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, Work(), CancellationToken.None);
        var free = await ShardTaskRunner.RunAsync(Task(m_origin.ShardUrl(SHARD), declared: 0), m_fetcher, m_blobs, Work(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(capped.Verdict, Is.EqualTo(CorpusShardVerdict.Oversized));
            Assert.That(capped.Error, Is.EqualTo(CorpusResultCodes.OVERSIZED), "past the cap, not past a declaration");
            Assert.That(free.Verdict, Is.EqualTo(CorpusShardVerdict.Completed));
        });
    }

    [TestCase(1L, 1L + 1024 * 1024)]
    [TestCase(1024L * 1024, 2L * 1024 * 1024)]
    [TestCase(80L * 1024 * 1024, 90L * 1024 * 1024)]
    [TestCase(0L, long.MaxValue)]
    [TestCase(-5L, long.MaxValue)]
    [TestCase(long.MaxValue, long.MaxValue)]
    public void H8TheBoundIsTheDeclarationAndAnEighthAMebibyteAtLeastTest(long declared, long bound)
    {
        Assert.That(ShardLimits.DeclaredCap(declared), Is.EqualTo(bound));
    }

    [Test]
    public void H8TheNodesCapIsTheSmallerOfTheCapAndTheDeclarationsBoundTest()
    {
        var declared = Task("https://data.commoncrawl.org/x.warc.wet.gz", 10 * 1024 * 1024);
        var huge = Task("https://data.commoncrawl.org/x.warc.wet.gz", 8L << 30);
        var none = Task("https://data.commoncrawl.org/x.warc.wet.gz", 0);
        var uncapped = Task("https://data.commoncrawl.org/x.warc.wet.gz", 0);
        uncapped.MaxShardBytes = 0;

        Assert.Multiple(() =>
        {
            Assert.That(ShardLimits.FetchCap(declared), Is.EqualTo(ShardLimits.DeclaredCap(10 * 1024 * 1024)));
            Assert.That(ShardLimits.DeclaredBinds(declared), Is.True);
            Assert.That(ShardLimits.FetchCap(huge), Is.EqualTo(CorpusTaskPlanner.MAX_SHARD_BYTES), "a declaration above the cap does not raise the cap");
            Assert.That(ShardLimits.DeclaredBinds(huge), Is.False);
            Assert.That(ShardLimits.FetchCap(none), Is.EqualTo(CorpusTaskPlanner.MAX_SHARD_BYTES));
            Assert.That(ShardLimits.FetchCap(uncapped), Is.EqualTo(CorpusTaskPlanner.MAX_SHARD_BYTES), "a task without a cap gets the node's own ceiling");
        });
    }

    [Test]
    public void H8AResultThatFetchedMoreThanItsDeclarationAllowsIsDroppedTest()
    {
        // A node that ignores the bound and processes the whole file cannot have its result
        // accepted: the server holds the result to the same number it gave the node.
        var task = CorpusTestRun.Task(0);
        task.DeclaredBytes = 100_000;
        var honest = CorpusTestRun.Completed(task);
        honest.Stats!.BytesFetched = ShardLimits.DeclaredCap(100_000);
        var greedy = CorpusTestRun.Completed(task);
        greedy.Stats!.BytesFetched = ShardLimits.DeclaredCap(100_000) + 1;

        var accepted = CorpusTestRun.Ledger([task], [honest]);
        var dropped = CorpusTestRun.Ledger([task], [greedy]);

        Assert.Multiple(() =>
        {
            Assert.That(accepted.Shards.Single().Accepted, Is.SameAs(honest));
            Assert.That(dropped.Shards.Single().Accepted, Is.Null);
            Assert.That(dropped.Rejected, Is.EqualTo(new Dictionary<string, int> { [CorpusLedger.REJECTED_CONTENT] = 1 }), "at odds with its task");
            Assert.That(dropped.RepairTasks().Single().DeclaredBytes, Is.EqualTo(100_000), "the shard goes out once more, under the same claim");
        });
    }

    [Test]
    public void H8TheCodeIsOneAResultMayCarryTest()
    {
        var task = CorpusTestRun.Task(0);

        Assert.Multiple(() =>
        {
            Assert.That(CorpusResultCodes.IsKnown(CorpusShardVerdict.Oversized, CorpusResultCodes.OVERSIZED_DECLARED), Is.True);
            Assert.That(CorpusResultCodes.CodesOf(CorpusShardVerdict.Oversized), Does.Contain(CorpusResultCodes.OVERSIZED_DECLARED));
            Assert.That(CorpusResultCheck.IsWellFormed(CorpusTestRun.Verdict(task, CorpusShardVerdict.Oversized, CorpusResultCodes.OVERSIZED_DECLARED)), Is.True);
            Assert.That(CorpusResultCheck.IsWellFormed(CorpusTestRun.Verdict(task, CorpusShardVerdict.NodeFault, CorpusResultCodes.OVERSIZED_DECLARED)), Is.False, "it is no fault of a node");
        });
    }

    #endregion

    #region Tools

    private CorpusTaskData Task(string url, long declared)
    {
        return new CorpusTaskData
        {
            TaskIndex = 0,
            Source = new CorpusSourceData { Url = url, Format = CorpusSourceFormat.Wet, SizeBytes = Math.Max(0, declared) },
            Pipeline = new CorpusPipelineData { Version = "h8", IdentifyLanguage = false },
            AllowedOrigins = ["127.0.0.1", "data.commoncrawl.org"],
            MaxShardBytes = CorpusTaskPlanner.MAX_SHARD_BYTES,
            Ticket = CorpusTickets.New(),
            RequiredControllerVersion = CorpusTestRun.VERSION,
            DeclaredBytes = declared
        };
    }

    private string Work()
    {
        return Path.Combine(m_root, "work", Guid.NewGuid().ToString("N"));
    }

    #endregion
}
