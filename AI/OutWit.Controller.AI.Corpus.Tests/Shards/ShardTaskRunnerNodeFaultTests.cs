using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Pdf;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Trust;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// A node's own trouble on a shard (audit findings M-1, H-7, H-9 and the Low items L-3a, L-5): a
/// file it cannot write, an upload that does not go through, a PDF its machine stopped, a share
/// larger than its disk. None of them is a verdict on the shard, none of them carries a path or
/// an exception's text to the lab, and none of them costs the node the shards it has finished.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ShardTaskRunnerNodeFaultTests
{
    #region Fields

    private string m_root = null!;
    private string m_shards = null!;
    private CorpusTestOrigin m_origin = null!;
    private CorpusTestBlobService m_blobs = null!;
    private ShardFetcher m_fetcher = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witai_corpus_fault_{Guid.NewGuid():N}");
        m_shards = Path.Combine(m_root, "shards");
        Directory.CreateDirectory(m_shards);
        WarcSynth.WriteWetShard(Path.Combine(m_shards, "s.warc.wet.gz"), 300, 300, 11);
        m_origin = new CorpusTestOrigin(m_shards);
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

    #region M-1 Tests

    [Test]
    public async Task M1AProductFileThatCannotBeWrittenIsANodeFaultWithoutAPathTest()
    {
        // The audit's reproduction: a folder in the product file's place. The verdict was Failed
        // and the report's error read "Access to the path 'C:\Users\<account>\...' is denied."
        var work = Work();
        Directory.CreateDirectory(Path.Combine(work, "text.jsonl.zst"));

        var result = await ShardTaskRunner.RunAsync(Task(), m_fetcher, m_blobs, work, CancellationToken.None);

        AssertNodeFault(result, CorpusResultCodes.FAULT_DISK, work);
    }

    [Test]
    public async Task M1AShardFileThatCannotBeWrittenIsANodeFaultNotAnExceptionTest()
    {
        // The audit's second case: the same for shard.gz threw UnauthorizedAccessException out of
        // the task and failed the node's whole batch.
        var work = Work();
        Directory.CreateDirectory(Path.Combine(work, "shard.gz"));

        var result = await ShardTaskRunner.RunAsync(Task(), m_fetcher, m_blobs, work, CancellationToken.None);

        AssertNodeFault(result, CorpusResultCodes.FAULT_DISK, work);
    }

    [Test]
    public async Task M1AnUploadThatDoesNotGoThroughIsANodeFaultOfThatShardTest()
    {
        // It threw, and the node lost every shard of its batch with it.
        var failing = new CorpusFlakyBlobService(m_blobs, uploadFailures: int.MaxValue / 2);
        var work = Work();

        var result = await RunWithoutUploadRetriesAsync(failing, work, CancellationToken.None);

        Assert.That(failing.Uploads, Is.EqualTo(3), "tried, and tried again");
        AssertNodeFault(result, CorpusResultCodes.FAULT_UPLOAD, work);
    }

    [Test]
    public async Task M1ANodeFaultIsRepairedOrReportedNeverAVerdictTest()
    {
        var work = Work();
        Directory.CreateDirectory(Path.Combine(work, "text.jsonl.zst"));
        var task = Task();
        var fault = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, work, CancellationToken.None);

        var first = CorpusLedger.Build(CorpusTestRun.VERSION, new CorpusRound(CorpusRoundKind.First, [task], [fault]));
        var repair = first.RepairTasks();
        var again = await ShardTaskRunner.RunAsync(repair[0], m_fetcher, m_blobs, Work(), CancellationToken.None);
        var ledger = CorpusLedger.Build(CorpusTestRun.VERSION, new CorpusRound(CorpusRoundKind.First, [task], [fault]), new CorpusRound(CorpusRoundKind.Repair, repair, [again]));
        var report = System.Text.Encoding.UTF8.GetString(CorpusReportWriter.Write(ledger));

        Assert.Multiple(() =>
        {
            Assert.That(repair, Has.Count.EqualTo(1), "the faulted shard is sent out once more");
            Assert.That(again.Verdict, Is.EqualTo(CorpusShardVerdict.Completed), again.Error);
            Assert.That(ledger.Shards.Single().Accepted, Is.SameAs(again));
            Assert.That(report, Does.Contain("\"verdict\": \"Completed\""));
            Assert.That(report, Does.Contain("\"disk\""), "the report says a node had trouble with it, by code");
            Assert.That(report, Does.Not.Contain("NodeFault").And.Not.Contain(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)));
        });
    }

    #endregion

    #region H-7 Tests

    [Test]
    public async Task H7ANodeHoldsItsDegreeAndTwoMoreShardsOnDiskNotItsShareTest()
    {
        // The audit's batch: four times the degree in tasks, each fetched before it waited for a
        // processing slot; at the peak every shard of the batch sat on disk together.
        var degree = ShardDegree.ForCurrentMachine();
        var count = degree * 4;
        var work = Work();

        var tasks = Enumerable.Range(0, count)
            .Select(index => ShardTaskRunner.RunAsync(Task(index), m_fetcher, m_blobs, Path.Combine(work, index.ToString()), CancellationToken.None))
            .ToList();

        // What the adapter does per task: the folder goes when the task ends.
        var cleaned = tasks.Select((task, index) => task.ContinueWith(_ => Directory.Delete(Path.Combine(work, index.ToString()), recursive: true), TaskScheduler.Default)).ToList();

        var peak = 0;
        var all = System.Threading.Tasks.Task.WhenAll(cleaned);
        while (!all.IsCompleted)
        {
            try
            {
                var held = Directory.Exists(work) ? Directory.GetFiles(work, "shard.gz", SearchOption.AllDirectories).Length : 0;
                peak = Math.Max(peak, held);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // a folder went while it was listed
            }

            await System.Threading.Tasks.Task.Delay(10);
        }

        await all;

        Assert.Multiple(() =>
        {
            Assert.That(tasks.Select(task => task.Result.Verdict), Is.All.EqualTo(CorpusShardVerdict.Completed));
            Assert.That(peak, Is.GreaterThan(0));
            Assert.That(peak, Is.LessThanOrEqualTo(degree + ShardTaskRunner.FETCH_AHEAD), $"a batch of {count} on a node of degree {degree}");
            Assert.That(ShardTaskRunner.FETCH_AHEAD, Is.EqualTo(2));
        });
    }

    #endregion

    #region L-5 Tests

    [TestCase(0L)]
    [TestCase(-1L)]
    [TestCase(long.MaxValue)]
    [TestCase((1L << 30) + 1)]
    public void L5ATaskWithoutACapIsHeldToTheNodesOwnCeilingTest(long taskCap)
    {
        // A task not made by Corpus.Split: 0 meant "no cap" to the fetcher.
        Assert.That(CorpusTaskPlanner.NodeShardCap(taskCap), Is.EqualTo(CorpusTaskPlanner.MAX_SHARD_BYTES));
    }

    [Test]
    public async Task L5TheNodesCeilingIsTheOneTheFetchRunsUnderTest()
    {
        var capped = Task();
        capped.MaxShardBytes = 1000;
        var uncapped = Task();
        uncapped.MaxShardBytes = 0;

        var small = await ShardTaskRunner.RunAsync(capped, m_fetcher, m_blobs, Work(), CancellationToken.None);
        var whole = await ShardTaskRunner.RunAsync(uncapped, m_fetcher, m_blobs, Work(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(CorpusTaskPlanner.NodeShardCap(1000), Is.EqualTo(1000), "a task's own cap within the ceiling is kept");
            Assert.That(small.Verdict, Is.EqualTo(CorpusShardVerdict.Oversized));
            Assert.That(small.Error, Is.EqualTo(CorpusResultCodes.OVERSIZED));
            Assert.That(whole.Verdict, Is.EqualTo(CorpusShardVerdict.Completed), "under the ceiling of 1 GiB, not without one");
        });
    }

    #endregion

    #region H-9 Tests

    [Test]
    public async Task H9APdfTheMachineStoppedIsANodeFaultOfThatShardTest()
    {
        // A wall-clock backstop of one millisecond stands in for a machine too slow or too busy to
        // finish a PDF within it: the run ends with fuel left. It threw, the node's batch was
        // lost with its finished shards, and the node was kept out of the job.
        var module = PdfTestModule.Require();
        using var slow = new PdfSandbox(module, PdfStageLimits.FUEL, PdfStageLimits.MEMORY_BYTES, wallMs: 1);
        var shards = Path.Combine(m_root, "pdf-shards");
        Directory.CreateDirectory(shards);
        var bomb = PdfHostileSet.Build().First(entry => entry.Name == "pages-fanout-1m.pdf").Bytes;
        await File.WriteAllBytesAsync(Path.Combine(shards, "p.warc.gz"), WarcSynth.Shard([WarcSynth.PdfResponseRecord("https://a.example/slow.pdf", bomb)]));
        using var origin = new CorpusTestOrigin(shards);
        var task = Task();
        task.Source = new CorpusSourceData { Url = origin.ShardUrl("p.warc.gz"), Format = CorpusSourceFormat.Warc };
        var work = Work();

        var result = await ShardTaskRunner.RunAsync(task, m_fetcher, m_blobs, work, null, null, new ShardPdfStage(slow, null, work), null, CancellationToken.None);

        AssertNodeFault(result, CorpusResultCodes.FAULT_PDF_BACKSTOP, work);
    }

    [Test]
    public void H9ThePdfFuelOfOneShardIsBoundedTest()
    {
        // The audit's last scenario: N documents that each burn their whole budget cost N
        // budgets, and a gigabyte holds 180,000 of the 5.7 KB one. A shard's PDFs have one
        // budget together (a hundred full ones; here three, with a sandbox of a small one).
        var module = PdfTestModule.Require();
        const long fuel = 2_000_000;
        using var starved = new PdfSandbox(module, fuel, PdfStageLimits.MEMORY_BYTES);
        var pdf = PdfSynth.Document([["A page that needs more fuel than this sandbox gives."]]);
        var shard = WarcSynth.Shard(Enumerable.Range(0, 7).Select(index => WarcSynth.PdfResponseRecord($"https://a.example/{index}.pdf", pdf)));
        var work = Work();
        var pipeline = new CorpusPipelineData { Version = "h9", IdentifyLanguage = false };

        var product = new ShardProcessor(pipeline, null, new ShardPdfStage(starved, null, work) { ShardFuel = 3 * fuel })
            .Process(new MemoryStream(shard), CorpusSourceFormat.Warc, new MemoryStream(), new MemoryStream());
        var stats = product.Stats.Build(0, 0, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(stats.Pdf!.In, Is.EqualTo(7), "every PDF is counted");
            Assert.That(stats.Pdf.Fuel, Is.EqualTo(3 * fuel), "three were run, each to the end of its fuel");
            Assert.That(stats.Drops.Select(drop => (drop.Name, drop.Count)), Is.EquivalentTo(new[] { ("pdf_fuel", 3), ("pdf_shard_fuel", 4) }), "the other four were not run");
            Assert.That(ShardDropRules.StageOf("pdf_shard_fuel"), Is.EqualTo(ShardDropRules.STAGE_EXTRACT));
            Assert.That(PdfStageLimits.SHARD_FUEL, Is.EqualTo(100 * PdfStageLimits.FUEL));
            Assert.That(PdfStageLimits.DESCRIPTOR, Does.Contain($"shard_fuel={PdfStageLimits.SHARD_FUEL}"), "a part of the stage set's fingerprint");
        });
    }

    [Test]
    public async Task L3aThePdfGuestRunsOnTheShardsThreadBelowNormalPriorityTest()
    {
        // The guest ran on a thread made for the call, at normal priority, while the lowered one
        // waited for it. On the shard's thread the host runs the guest where it is called.
        var observed = await ShardWorker.RunAsync(() =>
        {
            var caller = Environment.CurrentManagedThreadId;
            var guest = WasmHostGuestThread.Run(() => (Thread: Environment.CurrentManagedThreadId, Thread.CurrentThread.Priority));
            return (Caller: caller, Guest: guest.Thread, guest.Priority, ShardWorker.IsCurrent);
        });

        Assert.Multiple(() =>
        {
            Assert.That(observed.Guest, Is.EqualTo(observed.Caller), "no thread is made for the PDF");
            Assert.That(observed.Priority, Is.EqualTo(ThreadPriority.BelowNormal));
            Assert.That(observed.IsCurrent, Is.True);
        });
    }

    #endregion

    #region Tools

    /// <summary>A node fault is a result of its own kind: a code, the ticket, and nothing of the product or of the machine.</summary>
    private static void AssertNodeFault(CorpusResultData result, string code, string work)
    {
        var everything = string.Join('|', result.Error, result.SourceUrl, result.RuntimeTarget, result.ControllerVersion, result.NodeName);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.NodeFault));
            Assert.That(result.Error, Is.EqualTo(code), "a fixed code");
            Assert.That(result.Stats, Is.Null);
            Assert.That(result.ContentSha256, Is.Empty);
            Assert.That(result.TextBlobId, Is.EqualTo(Guid.Empty));
            Assert.That(result.SignaturesBlobId, Is.EqualTo(Guid.Empty));
            Assert.That(result.Ticket, Has.Length.EqualTo(32), "the server knows which shard came back unanswered");
            Assert.That(CorpusResultCheck.IsWellFormed(result), Is.True);
            Assert.That(everything, Does.Not.Contain(work).And.Not.Contain(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)).And.Not.Contain(Environment.UserName).And.Not.Contain("denied").And.Not.Contain("Exception"));
        });
    }

    private CorpusTaskData Task(int index = 0)
    {
        return new CorpusTaskData
        {
            TaskIndex = index,
            Source = new CorpusSourceData { Url = m_origin.ShardUrl("s.warc.wet.gz"), Format = CorpusSourceFormat.Wet },
            Pipeline = new CorpusPipelineData { Version = "fault", IdentifyLanguage = false },
            AllowedOrigins = [m_origin.Host],
            MaxShardBytes = 1L << 30,
            Ticket = CorpusTickets.New(),
            RequiredControllerVersion = CorpusTestRun.VERSION
        };
    }

    /// <summary>The upload's own retries take about five minutes; the test has two of them, at once.</summary>
    private async Task<CorpusResultData> RunWithoutUploadRetriesAsync(CorpusFlakyBlobService blobs, string work, CancellationToken cancellationToken)
    {
        var delays = ShardBlobTransfer.Delays;
        ShardBlobTransfer.Delays = [TimeSpan.Zero, TimeSpan.Zero];
        try
        {
            return await ShardTaskRunner.RunAsync(Task(), m_fetcher, blobs, work, cancellationToken);
        }
        finally
        {
            ShardBlobTransfer.Delays = delays;
        }
    }

    private string Work()
    {
        return Path.Combine(m_root, "work", Guid.NewGuid().ToString("N"));
    }

    #endregion
}
