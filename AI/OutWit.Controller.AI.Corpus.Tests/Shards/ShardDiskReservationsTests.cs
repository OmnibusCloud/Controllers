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

        var sized = Task(origin, size);
        var unsized = Task(origin, 0);
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
    public void ThePackageGuardAsksForTheCapWhenAShardOfThePackageHasNoSizeTest()
    {
        var package = new CorpusTaskPackageData { LargestShardBytes = 10 * MIB, UnsizedShards = 1 };
        var sized = new CorpusTaskPackageData { LargestShardBytes = 10 * MIB, UnsizedShards = 0 };

        Assert.Multiple(() =>
        {
            Assert.That(ShardBatchGuard.LargestShardBytes(package), Is.EqualTo(1L << 30));
            Assert.That(ShardBatchGuard.LargestShardBytes(sized), Is.EqualTo(10 * MIB + 10 * MIB / 8), "the declared size and its slack, an eighth of it");
            Assert.That(ShardDisk.Needed(1L << 30), Is.EqualTo((1L << 30) + ShardProductLimits.MaxProductBytes(1L << 30, null) + ShardDisk.MARGIN_BYTES));
        });
    }

    #endregion

    #region Tools

    private static CorpusTaskData Task(CorpusTestOrigin origin, long size)
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
