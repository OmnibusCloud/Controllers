using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.AI.Corpus.Delivery;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Pdf;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Tests.Delivery;

/// <summary>
/// Audit finding M-5 on the node: a node's work comes as a task package (a blob) and goes back
/// as a result package (a blob); what it is handed as a parameter and what it returns are
/// references. The node runs only the blob its reference names, within its own bound on a
/// package, and a package it cannot answer as a whole goes back as a fault with a fixed code,
/// never as a failure with the node's words in it.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class CorpusPackageRunnerM5Tests
{
    #region Constants

    private const string SHARD = "s.warc.wet.gz";

    #endregion

    #region Fields

    private string m_root = null!;
    private CorpusTestOrigin m_origin = null!;
    private CorpusTestBlobService m_blobs = null!;
    private ShardFetcher m_fetcher = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witai_corpus_package_{Guid.NewGuid():N}");
        var shards = Path.Combine(m_root, "shards");
        Directory.CreateDirectory(shards);
        WarcSynth.WriteWetShard(Path.Combine(shards, SHARD), 40, 200, 5);
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
    public async Task M5APackageOfShardsComesBackAsOneBlobOfResultsTest()
    {
        var tasks = Enumerable.Range(0, 12).Select(Task).ToList();
        var package = Package(tasks);

        var answer = await RunAsync(package);
        var results = CorpusTestPackages.Results(m_blobs, answer);

        Assert.Multiple(() =>
        {
            Assert.That(answer.Fault, Is.Empty);
            Assert.That(answer.Ticket, Is.EqualTo(package.Ticket), "the answer brings the package's ticket back");
            Assert.That(results, Has.Count.EqualTo(12));
            Assert.That(results.Select(result => result.Verdict), Is.All.EqualTo(CorpusShardVerdict.Completed));
            Assert.That(results.Select(result => result.Ticket).Order(), Is.EqualTo(tasks.Select(task => task.Ticket).Order()), "one result for each shard of the package, under the shard's own ticket");
            Assert.That(results.Select(result => result.ContentSha256).Distinct().Count(), Is.EqualTo(1), "twelve listings of one shard: one product");
            Assert.That(results.Select(result => result.Degree), Is.All.Zero, "a result does not say what its machine is");
            Assert.That(results, Is.All.Matches<CorpusResultData>(CorpusResultCheck.IsWellFormed));
            Assert.That(MemoryPack.MemoryPackSerializer.Serialize(package).Length + MemoryPack.MemoryPackSerializer.Serialize(answer).Length, Is.LessThan(600), "what travelled as a parameter and as a return value: two references");
        });
    }

    [Test]
    public async Task M5AShardThatFailsIsOneResultOfThePackageNotThePackageTest()
    {
        var tasks = Enumerable.Range(0, 4).Select(Task).ToList();
        tasks[1].Source!.Url = m_origin.ShardUrl("missing.warc.wet.gz");
        tasks[2].Source!.Url = "https://data.commoncrawl.org/not-on-the-tasks-list.warc.wet.gz";

        var answer = await RunAsync(Package(tasks));
        var results = CorpusTestPackages.Results(m_blobs, answer).OrderBy(result => result.TaskIndex).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(answer.Fault, Is.Empty, "the package is answered");
            Assert.That(results.Select(result => result.Verdict), Is.EqualTo(new[] { CorpusShardVerdict.Completed, CorpusShardVerdict.FetchFailed, CorpusShardVerdict.OriginRefused, CorpusShardVerdict.Completed }));
            Assert.That(results[1].Error, Is.EqualTo("http_404"));
        });
    }

    [Test]
    public async Task M5ATaskBlobThatIsNotTheOneItsReferenceNamesGoesBackAsAFaultTest()
    {
        var tasks = Enumerable.Range(0, 3).Select(Task).ToList();
        var package = Package(tasks);
        var other = Package(Enumerable.Range(0, 3).Select(Task).ToList());

        var wrongHash = package.Clone();
        wrongHash.Sha256 = other.Sha256;
        var wrongBlob = package.Clone();
        wrongBlob.TasksBlobId = other.TasksBlobId;
        var wrongCount = package.Clone();
        wrongCount.ShardCount = 2;
        var noise = package.Clone();
        noise.TasksBlobId = await m_blobs.UploadBytesAsync(new byte[package.SizeBytes], "noise.wcp");
        var before = m_origin.Requests;

        var answers = new List<CorpusResultPackageData>();
        foreach (var reference in new[] { wrongHash, wrongBlob, wrongCount, noise })
            answers.Add(await RunAsync(reference));

        Assert.Multiple(() =>
        {
            Assert.That(answers.Select(answer => answer.Fault), Is.All.EqualTo(CorpusResultCodes.FAULT_TASKS), "a fixed code");
            Assert.That(answers.Select(answer => answer.ResultsBlobId), Is.All.EqualTo(Guid.Empty));
            Assert.That(answers.Select(answer => answer.Ticket), Is.All.EqualTo(package.Ticket), "under the package's ticket: the server knows which shards go out once more");
            Assert.That(m_origin.Requests, Is.EqualTo(before), "nothing of such a package is fetched");
        });
    }

    [Test]
    public async Task M5ATaskBlobThatDoesNotArriveGoesBackAsAFaultWithoutTheNodesWordsTest()
    {
        var package = Package(Enumerable.Range(0, 2).Select(Task).ToList());
        var failing = new CorpusFlakyBlobService(m_blobs, downloadFailures: int.MaxValue / 2);
        var delays = ShardBlobTransfer.Delays;
        ShardBlobTransfer.Delays = [TimeSpan.Zero];

        CorpusResultPackageData answer;
        try
        {
            answer = await CorpusPackageRunner.RunAsync(package, m_fetcher, failing, Scope(), Guid.NewGuid(), null, NullLogger.Instance, null, CancellationToken.None);
        }
        finally
        {
            ShardBlobTransfer.Delays = delays;
        }

        var everything = MemoryPack.MemoryPackSerializer.Serialize(answer);

        Assert.Multiple(() =>
        {
            Assert.That(answer.Fault, Is.EqualTo(CorpusResultCodes.FAULT_TASKS));
            Assert.That(System.Text.Encoding.UTF8.GetString(everything), Does.Not.Contain("blob channel").And.Not.Contain("Exception").And.Not.Contain(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)));
        });
    }

    [Test]
    public async Task M5ResultsThatCannotBeUploadedGoBackAsAFaultOfThePackageTest()
    {
        // The shards keep statistics and hashes only (nothing of theirs is uploaded); the upload
        // that fails is the one of the results blob.
        var tasks = Enumerable.Range(0, 2).Select(index =>
        {
            var task = Task(index);
            task.Pipeline!.Output = CorpusOutputMode.StatsAndHashes;
            return task;
        }).ToList();
        var failing = new CorpusFlakyBlobService(m_blobs, uploadFailures: int.MaxValue / 2);
        var delays = ShardBlobTransfer.Delays;
        ShardBlobTransfer.Delays = [TimeSpan.Zero];

        CorpusResultPackageData answer;
        try
        {
            answer = await CorpusPackageRunner.RunAsync(Package(tasks), m_fetcher, failing, Scope(), Guid.NewGuid(), null, NullLogger.Instance, null, CancellationToken.None);
        }
        finally
        {
            ShardBlobTransfer.Delays = delays;
        }

        Assert.Multiple(() =>
        {
            Assert.That(answer.Fault, Is.EqualTo(CorpusResultCodes.FAULT_UPLOAD));
            Assert.That(answer.ResultsBlobId, Is.EqualTo(Guid.Empty));
            Assert.That(failing.Uploads, Is.EqualTo(2), "tried, and tried again");
        });
    }

    [Test]
    public async Task M5ANodeHoldsAPackageToItsOwnBoundTest()
    {
        // A reference that states more shards than a package may hold: no blob is asked for.
        var package = Package(Enumerable.Range(0, 2).Select(Task).ToList());
        package.ShardCount = CorpusPackagePlanner.MAX_PACKAGE_SHARDS + 1;
        var counting = new CorpusFlakyBlobService(m_blobs);

        var answer = await CorpusPackageRunner.RunAsync(package, m_fetcher, counting, Scope(), Guid.NewGuid(), null, NullLogger.Instance, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(answer.Fault, Is.EqualTo(CorpusResultCodes.FAULT_TASKS));
            Assert.That(counting.Downloads, Is.Zero);
        });
    }

    [Test]
    public void M5TheBatchIsRefusedFromTheReferenceAloneTest()
    {
        // The three questions a node asks before any work, answered without opening the blob.
        var tasks = Enumerable.Range(0, 3).Select(Task).ToList();
        tasks[1].Source = new CorpusSourceData { Url = m_origin.ShardUrl(SHARD), Format = CorpusSourceFormat.Warc, SizeBytes = 5_000_000 };
        var package = Package(tasks);
        var wet = Package([Task(0)]);
        var asked = 0;

        string? NoSandbox()
        {
            asked++;
            return "no sandbox";
        }

        var version = package.Clone();
        version.RequiredControllerVersion = "0.0.1";

        Assert.Multiple(() =>
        {
            Assert.That(package.NeedsPdfSandbox, Is.True, "a WARC shard with the PDF stage on");
            Assert.That(package.LargestShardBytes, Is.EqualTo(5_000_000));
            Assert.That(package.ShardCount, Is.EqualTo(3));
            Assert.That(package.UnsizedShards, Is.EqualTo(2));
            Assert.That(wet.NeedsPdfSandbox, Is.False);

            Assert.That(ShardBatchGuard.Refusal(package, CorpusTestRun.VERSION, () => null, () => true), Is.Null);
            Assert.That(ShardBatchGuard.Refusal(version, CorpusTestRun.VERSION, () => null, () => true)!.Code, Is.EqualTo(ShardBatchRefusedException.VERSION));
            Assert.That(ShardBatchGuard.Refusal(package, CorpusTestRun.VERSION, NoSandbox, () => true)!.Code, Is.EqualTo(ShardBatchRefusedException.PDF_SANDBOX));
            Assert.That(ShardBatchGuard.Refusal(wet, CorpusTestRun.VERSION, NoSandbox, () => true), Is.Null);
            Assert.That(asked, Is.EqualTo(1), "a package without a WARC shard does not ask for the sandbox");
            Assert.That(ShardBatchGuard.Refusal(package, CorpusTestRun.VERSION, () => null, () => false)!.Code, Is.EqualTo(ShardBatchRefusedException.DISK_SPACE));
            Assert.That(ShardBatchGuard.Check(package, CorpusTestRun.VERSION, () => throw new IOException(@"C:\Users\someone\client"), () => true)!.Message, Does.Not.Contain("someone"));
        });
    }

    [Test]
    public void ANodeWithoutRoomForWhatItsTasksReserveRefusesTheBatchBeforeAFetchTest()
    {
        // The disk is asked what the package's tasks will reserve once they are read, before a
        // byte of a shard is fetched.
        var package = Package([Task(0), Task(1)]);
        var free = ShardDiskReservations.FreeBytes;
        var before = m_origin.Requests;
        ShardDiskReservations.FreeBytes = _ => ShardBatchGuard.RoomNeeded([Task(0)]) - 1;
        try
        {
            Assert.That(async () => await RunAsync(package), Throws.TypeOf<ShardBatchRefusedException>().With.Property("Code").EqualTo(ShardBatchRefusedException.DISK_SPACE));
            Assert.That(m_origin.Requests, Is.EqualTo(before), "nothing was fetched");
        }
        finally
        {
            ShardDiskReservations.FreeBytes = free;
            ShardBatchGuard.Clear();
        }
    }

    #endregion

    #region Tools

    private CorpusTaskData Task(int index)
    {
        return new CorpusTaskData
        {
            TaskIndex = index,
            Source = new CorpusSourceData { Url = m_origin.ShardUrl(SHARD), Format = CorpusSourceFormat.Wet },
            Pipeline = new CorpusPipelineData { Version = "package", IdentifyLanguage = false },
            AllowedOrigins = [m_origin.Host],
            MaxShardBytes = 1L << 30,
            Ticket = CorpusTickets.New(),
            RequiredControllerVersion = CorpusTestRun.VERSION
        };
    }

    private CorpusTaskPackageData Package(IReadOnlyList<CorpusTaskData> tasks)
    {
        // One package for all of them, as a job past 1,024 shards has.
        return CorpusTaskPackages.WriteAsync(m_blobs, tasks, tasks.Count * CorpusPackagePlanner.MAX_PACKAGES).GetAwaiter().GetResult().Single();
    }

    private Task<CorpusResultPackageData> RunAsync(CorpusTaskPackageData package)
    {
        return CorpusPackageRunner.RunAsync(package, m_fetcher, m_blobs, Scope(), Guid.NewGuid(), null, NullLogger.Instance, null, CancellationToken.None);
    }

    private string Scope()
    {
        return Path.Combine(m_root, "work", Guid.NewGuid().ToString("N"));
    }

    #endregion
}
