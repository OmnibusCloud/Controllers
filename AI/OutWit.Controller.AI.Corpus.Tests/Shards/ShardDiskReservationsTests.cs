using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// The disk question as a reservation. Each task asked the disk for its own shard once, after it
/// had its place in the window: every task of a window was told there was room for a shard of
/// half the disk, and a shard without a size was asked nothing. A task now reserves the most its
/// shard may take before it fetches, against the room the node's other shards hold, and a shard
/// without a size is asked for the cap it is held to. The disk here is the test's own.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ShardDiskReservationsTests
{
    #region Constants

    private const long MIB = 1024 * 1024;

    #endregion

    #region Fields

    private Func<string, long> m_freeBytes = null!;
    private string m_directory = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_freeBytes = ShardDiskReservations.FreeBytes;
        m_directory = Path.Combine(Path.GetTempPath(), $"witai_corpus_room_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        ShardDiskReservations.FreeBytes = m_freeBytes;
        CorpusTestFolders.Delete(m_directory);
    }

    #endregion

    #region Reservation Tests

    [Test]
    public async Task TwoShardsThatEachFitAloneAreNotBothToldThereIsRoomTest()
    {
        // The audit's case: a disk with room for one shard and a half, two shards of the window.
        var needed = 100 * MIB;
        ShardDiskReservations.FreeBytes = _ => needed + needed / 2;

        var first = await ShardDiskReservations.ReserveAsync(m_directory, needed, CancellationToken.None);
        var second = ShardDiskReservations.ReserveAsync(m_directory, needed, CancellationToken.None);

        await Task.Delay(200);
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(second.IsCompleted, Is.False, "the second waits for the room the first holds");
            Assert.That(ShardDiskReservations.Reserved, Is.EqualTo(needed));
        });

        first!.Dispose();
        var finished = await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.That(finished, Is.SameAs(second), "the room came back");
        using var held = await second;
        Assert.That(held, Is.Not.Null);
    }

    [Test]
    public async Task AShardThatDoesNotFitWithNothingElseReservedIsRefusedAtOnceTest()
    {
        ShardDiskReservations.FreeBytes = _ => 10 * MIB;

        var room = await ShardDiskReservations.ReserveAsync(m_directory, 11 * MIB, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(room, Is.Null);
            Assert.That(ShardDiskReservations.Reserved, Is.Zero);
        });
    }

    [Test]
    public async Task AVolumeThatDoesNotSayTakesEveryShardTest()
    {
        ShardDiskReservations.FreeBytes = _ => -1;

        using var first = await ShardDiskReservations.ReserveAsync(m_directory, long.MaxValue / 4, CancellationToken.None);
        using var second = await ShardDiskReservations.ReserveAsync(m_directory, long.MaxValue / 4, CancellationToken.None);

        Assert.That(first != null && second != null, Is.True);
    }

    [Test]
    public void AWaitForRoomEndsWithTheJobTest()
    {
        ShardDiskReservations.FreeBytes = _ => 150 * MIB;
        using var first = ShardDiskReservations.ReserveAsync(m_directory, 100 * MIB, CancellationToken.None).Result;
        using var job = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        Assert.That(async () => await ShardDiskReservations.ReserveAsync(m_directory, 100 * MIB, job.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    #endregion

    #region Task Tests

    [Test]
    public async Task AShardWithoutASizeIsAskedForTheCapItIsHeldToTest()
    {
        // Room for a shard of its declared size and its products, not for one of the cap.
        var shards = Path.Combine(m_directory, "shards");
        Directory.CreateDirectory(shards);
        WarcSynth.WriteWetShard(Path.Combine(shards, "s.warc.wet.gz"), 40, 200, 5);
        var size = new FileInfo(Path.Combine(shards, "s.warc.wet.gz")).Length;
        using var origin = new CorpusTestOrigin(shards);
        var blobs = new CorpusTestBlobService(Path.Combine(m_directory, "blobs"));
        var fetcher = new ShardFetcher(ShardFetchPolicy.Immediate, CorpusTestOrigin.Policy);

        var sized = TaskFor(origin, size);
        var unsized = TaskFor(origin, 0);
        ShardDiskReservations.FreeBytes = _ => ShardDisk.Needed(ShardBatchGuard.RoomBytes(sized), sized.Pipeline) + MIB;

        var withSize = await ShardTaskRunner.RunAsync(sized, fetcher, blobs, Path.Combine(m_directory, "w1"), CancellationToken.None);
        var withoutSize = await ShardTaskRunner.RunAsync(unsized, fetcher, blobs, Path.Combine(m_directory, "w2"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(withSize.Verdict, Is.EqualTo(CorpusShardVerdict.Completed), withSize.Error);
            Assert.That(withoutSize.Verdict, Is.EqualTo(CorpusShardVerdict.NodeFault));
            Assert.That(withoutSize.Error, Is.EqualTo(CorpusResultCodes.FAULT_DISK_SPACE), "asked for the 1 GiB it may be, and its products");
            Assert.That(ShardBatchGuard.RoomBytes(unsized), Is.EqualTo(1L << 30));
            Assert.That(ShardDiskReservations.Reserved, Is.Zero, "every room given back");
        });
    }

    [Test]
    public async Task AProductAnUploadStillReadsStaysUntilTheUploadEndsTest()
    {
        // The job ends while the upload of a product still runs (its call takes no token): the
        // product stays on the disk until the call ends; its room is given back with the task.
        var held = new CorpusHeldBlobService(new CorpusTestBlobService(Path.Combine(m_directory, "blobs")));
        var (task, fetcher, origin) = HeldShard();
        var work = Path.Combine(m_directory, "held");
        using var job = new CancellationTokenSource();

        try
        {
            var running = ShardTaskRunner.RunAsync(task, fetcher, held, work, job.Token);
            await WaitFor(() => held.Uploads > 0);
            job.Cancel();

            Assert.That(async () => await running, Throws.InstanceOf<OperationCanceledException>());
            await HeldUntilReleasedAsync(held, work);
        }
        finally
        {
            held.Release();
            origin.Dispose();
        }
    }

    [Test]
    public async Task AProductAnUploadGaveUpOnStaysUntilTheCallEndsTest()
    {
        // The upload's call outlasts its schedule: the task is the fault `upload`, and the call
        // still reads the product, so the product stays until the call ends; its room is given
        // back with the task.
        var held = new CorpusHeldBlobService(new CorpusTestBlobService(Path.Combine(m_directory, "blobs")));
        var (task, fetcher, origin) = HeldShard();
        var work = Path.Combine(m_directory, "given-up");
        var baseTime = ShardBlobTransfer.BaseTime;
        var delays = ShardBlobTransfer.Delays;
        ShardBlobTransfer.BaseTime = TimeSpan.FromMilliseconds(200);
        ShardBlobTransfer.Delays = [];

        try
        {
            var result = await ShardTaskRunner.RunAsync(task, fetcher, held, work, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.Verdict, Is.EqualTo(CorpusShardVerdict.NodeFault));
                Assert.That(result.Error, Is.EqualTo(CorpusResultCodes.FAULT_UPLOAD));
                Assert.That(held.Uploads, Is.EqualTo(1), "one call, given up and not repeated");
            });
            await HeldUntilReleasedAsync(held, work);
        }
        finally
        {
            ShardBlobTransfer.BaseTime = baseTime;
            ShardBlobTransfer.Delays = delays;
            held.Release();
            origin.Dispose();
        }
    }

    [Test]
    public async Task AnUploadCallThatNeverEndsLeavesNoShardWaitingForItsRoomTest()
    {
        // A disk with room for the shard and a mebibyte more. The shard's upload call never ends
        // (until the test lets it go): the task is the fault `upload`, its product stays for the
        // call. Its room held until the call ended, a later shard that did not fit waited for it
        // until its job was cancelled, neither refused nor run.
        var held = new CorpusHeldBlobService(new CorpusTestBlobService(Path.Combine(m_directory, "blobs")));
        var (task, fetcher, origin) = HeldShard();
        var work = Path.Combine(m_directory, "never-ends");
        var needed = ShardDisk.Needed(ShardBatchGuard.RoomBytes(task), task.Pipeline);
        ShardDiskReservations.FreeBytes = _ => needed + MIB;
        var baseTime = ShardBlobTransfer.BaseTime;
        var delays = ShardBlobTransfer.Delays;
        ShardBlobTransfer.BaseTime = TimeSpan.FromMilliseconds(200);
        ShardBlobTransfer.Delays = [];
        using var job = new CancellationTokenSource();

        try
        {
            var result = await ShardTaskRunner.RunAsync(task, fetcher, held, work, CancellationToken.None);
            var later = ShardDiskReservations.ReserveAsync(m_directory, needed + 2 * MIB, job.Token);
            var answered = await Task.WhenAny(later, Task.Delay(TimeSpan.FromSeconds(5))) == later;

            Assert.Multiple(() =>
            {
                Assert.That(result.Error, Is.EqualTo(CorpusResultCodes.FAULT_UPLOAD));
                Assert.That(ShardBlobTransfer.HeldBy(Path.Combine(work, "text.jsonl.zst")), Is.Not.Null, "the call still reads the product");
                Assert.That(answered, Is.True, "a shard that does not fit is answered at once, not left waiting for the call");
                Assert.That(answered && later.Result == null, Is.True, "and refused (disk_space): nothing holds room it could wait for");
            });

            var fits = await ShardDiskReservations.ReserveAsync(m_directory, needed, CancellationToken.None);
            Assert.That(fits, Is.Not.Null, "a shard that fits is taken");
            fits!.Dispose();
        }
        finally
        {
            job.Cancel();
            ShardBlobTransfer.BaseTime = baseTime;
            ShardBlobTransfer.Delays = delays;
            held.Release();
            origin.Dispose();
        }
    }

    [Test]
    public void TheBatchIsRefusedForTheRoomItsTasksWillReserveTest()
    {
        // A shard without a size signed with 1,024 hashes reserves the cap, twice the cap in text
        // and a 1,024-hash signature for every document the cap may give: about 19 GiB. Asked
        // what a 128-hash shard needs (about 5 GiB), a node with 10 GiB took the batch and then
        // faulted every task for room.
        var job = Guid.NewGuid();
        var wide = new CorpusTaskData
        {
            TaskIndex = 0,
            Source = new CorpusSourceData { Url = "https://data.commoncrawl.org/x.warc.wet.gz", Format = CorpusSourceFormat.Wet },
            Pipeline = new CorpusPipelineData { Version = "room", MinHash = new CorpusMinHashData { Hashes = 1024, ShingleWords = 5, Bands = 32 } },
            MaxShardBytes = 1L << 30,
            Ticket = CorpusTickets.New(),
            RequiredControllerVersion = CorpusTestRun.VERSION
        };
        ShardDiskReservations.FreeBytes = _ => 10L << 30;

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(ShardBatchGuard.RoomNeeded([wide]), Is.GreaterThan(18L << 30));
                Assert.That(() => ShardBatchGuard.ThrowIfNoRoom(job, [wide], m_directory), Throws.TypeOf<ShardBatchRefusedException>().With.Property("Code").EqualTo(ShardBatchRefusedException.DISK_SPACE));
                Assert.That(() => ShardBatchGuard.ThrowIfJobRefused(job), Throws.TypeOf<ShardBatchRefusedException>(), "the batch's other tasks stop too");
            });
        }
        finally
        {
            ShardBatchGuard.Clear();
        }
    }

    [Test]
    public void ABatchThatFitsIsNotRefusedForRoomItWillNotReserveTest()
    {
        // An unsigned pipeline and a job cap of 10 MiB, shards without a size: each reserves the
        // job's cap and its text, a few dozen MiB. Asked about the node's own cap and 128-hash
        // signatures, the node refused the batch, and the job's other packages with it.
        var job = Guid.NewGuid();
        var narrow = new CorpusTaskData
        {
            TaskIndex = 0,
            Source = new CorpusSourceData { Url = "https://data.commoncrawl.org/x.warc.wet.gz", Format = CorpusSourceFormat.Wet },
            Pipeline = new CorpusPipelineData { Version = "room", Sign = false },
            MaxShardBytes = 10 * MIB,
            Ticket = CorpusTickets.New(),
            RequiredControllerVersion = CorpusTestRun.VERSION
        };
        var needed = ShardBatchGuard.RoomNeeded([narrow]);
        ShardDiskReservations.FreeBytes = _ => needed + MIB;

        Assert.Multiple(() =>
        {
            Assert.That(needed, Is.EqualTo(10 * MIB + ShardProductLimits.MaxTextBytes(10 * MIB) + ShardDisk.MARGIN_BYTES), "the job's cap, its text, the margin: no signatures");
            Assert.That(() => ShardBatchGuard.ThrowIfNoRoom(job, [narrow], m_directory), Throws.Nothing);
            Assert.That(() => ShardBatchGuard.ThrowIfJobRefused(job), Throws.Nothing);
        });
    }

    #endregion

    #region Tools

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var waited = 0; waited < 200 && !condition(); waited++)
            await Task.Delay(50);
    }

    /// <summary>A sized WET shard on a local origin, for a task whose upload the test holds.</summary>
    private (CorpusTaskData Shard, ShardFetcher Fetcher, CorpusTestOrigin Origin) HeldShard()
    {
        var shards = Path.Combine(m_directory, "shards");
        Directory.CreateDirectory(shards);
        WarcSynth.WriteWetShard(Path.Combine(shards, "s.warc.wet.gz"), 40, 200, 5);
        var size = new FileInfo(Path.Combine(shards, "s.warc.wet.gz")).Length;
        var origin = new CorpusTestOrigin(shards);
        return (TaskFor(origin, size), new ShardFetcher(ShardFetchPolicy.Immediate, CorpusTestOrigin.Policy), origin);
    }

    /// <summary>The product stays while the upload's call runs and goes when it ends; its room went with the task.</summary>
    private static async Task HeldUntilReleasedAsync(CorpusHeldBlobService held, string work)
    {
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(work, "text.jsonl.zst")), Is.True, "the product stays while the upload's call reads it");
            Assert.That(ShardDiskReservations.Reserved, Is.Zero, "its room is given back with the task: the product's bytes are on the volume, which the free room counts");
        });

        held.Release();
        await WaitFor(() => !File.Exists(Path.Combine(work, "text.jsonl.zst")));

        Assert.That(File.Exists(Path.Combine(work, "text.jsonl.zst")), Is.False, "the product goes when the call ends");
    }

    private static CorpusTaskData TaskFor(CorpusTestOrigin origin, long size)
    {
        return new CorpusTaskData
        {
            TaskIndex = 0,
            Source = new CorpusSourceData { Url = origin.ShardUrl("s.warc.wet.gz"), Format = CorpusSourceFormat.Wet, SizeBytes = size },
            Pipeline = new CorpusPipelineData { Version = "room", IdentifyLanguage = false },
            AllowedOrigins = [origin.Host],
            MaxShardBytes = 1L << 30,
            DeclaredBytes = size,
            Ticket = CorpusTickets.New(),
            RequiredControllerVersion = CorpusTestRun.VERSION
        };
    }

    #endregion
}
