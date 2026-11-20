using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// A transfer over the node's blob channel that never returns. The channel's calls take no
/// token; while the gate is closed (a new process, or after a failure) transfers pass one at a
/// time, so such a call held every later Corpus transfer of the node, each job's until its own
/// cancellation. Every attempt now has a time to finish in, and the job's token ends the wait.
/// </summary>
[TestFixture]
public sealed class ShardBlobTransferTimeoutTests
{
    #region Fields

    private string m_directory = null!;
    private CorpusTestBlobService m_blobs = null!;
    private IReadOnlyList<TimeSpan> m_delays = null!;
    private TimeSpan m_baseTime;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witai_corpus_transfer_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
        m_blobs = new CorpusTestBlobService(Path.Combine(m_directory, "blobs"));
        m_delays = ShardBlobTransfer.Delays;
        m_baseTime = ShardBlobTransfer.BaseTime;

        // One attempt a transfer: a failure is final at once.
        ShardBlobTransfer.Delays = [];
    }

    [TearDown]
    public void TearDown()
    {
        ShardBlobTransfer.Delays = m_delays;
        ShardBlobTransfer.BaseTime = m_baseTime;

        if (Directory.Exists(m_directory))
            Directory.Delete(m_directory, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public async Task ATransferThatNeverReturnsFailsWhenItsTimeIsUpAndHoldsNoOtherJobTest()
    {
        ShardBlobTransfer.BaseTime = TimeSpan.FromMilliseconds(300);
        var blobId = await UploadAsync();
        CloseTheGate(blobId);

        var hanging = new CorpusHangingBlobService(m_blobs, hangs: 1);
        var stuck = ShardBlobTransfer.DownloadAsync(hanging, blobId, 0, null, CancellationToken.None);
        var other = ShardBlobTransfer.DownloadAsync(m_blobs, blobId, 0, null, CancellationToken.None);

        var finished = await Task.WhenAny(other, Task.Delay(TimeSpan.FromSeconds(15)));

        Assert.Multiple(() =>
        {
            Assert.That(finished, Is.SameAs(other), "another job's transfer went through: the gate is free");
            Assert.That(async () => await stuck, Throws.InstanceOf<IOException>(), "the attempt that never returned counts as failed once its schedule is spent: its shard becomes a fault of this node");
            Assert.That(hanging.Hung, Is.EqualTo(1), "the first call was taken and never answered");
        });
    }

    [Test]
    public async Task AnAttemptThatRanOutOfItsTimeIsWaitedForAndNoSecondOneStartsBesideItTest()
    {
        // An upload that takes longer than its attempt's time and then succeeds: retried at once,
        // the same file went up again beside the call still sending it (five times here), each
        // a blob, and the transfer failed all the same.
        ShardBlobTransfer.BaseTime = TimeSpan.FromMilliseconds(200);
        ShardBlobTransfer.Delays = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero];
        var path = Path.Combine(m_directory, "product.bin");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        var slow = new CorpusSlowBlobService(m_blobs, TimeSpan.FromMilliseconds(500));

        var blobId = await ShardBlobTransfer.UploadAsync(slow, path, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(slow.Uploads, Is.EqualTo(1), "one call for the file: the one that ran out of its time was waited for");
            Assert.That(blobId, Is.EqualTo(slow.First), "and its answer is the transfer's");
            Assert.That(ShardBlobTransfer.HeldBy(path), Is.Null);
        });
    }

    [Test]
    public async Task ACallThatOutlastsTheScheduleFailsTheTransferAndHoldsItsFileUntilItEndsTest()
    {
        // Three attempts' time spent on one call: the transfer fails as before, with one call
        // made, and the call still reads the file.
        ShardBlobTransfer.BaseTime = TimeSpan.FromMilliseconds(200);
        ShardBlobTransfer.Delays = [TimeSpan.Zero, TimeSpan.Zero];
        var path = Path.Combine(m_directory, "outlasting.bin");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        var held = new CorpusHeldBlobService(m_blobs);

        try
        {
            Assert.That(async () => await ShardBlobTransfer.UploadAsync(held, path, null, CancellationToken.None), Throws.InstanceOf<IOException>());
            Assert.Multiple(() =>
            {
                Assert.That(held.Uploads, Is.EqualTo(1), "no second call beside the first");
                Assert.That(ShardBlobTransfer.HeldBy(path), Is.Not.Null, "the call still reads the file");
            });

            held.Release();
            await WaitFor(() => ShardBlobTransfer.HeldBy(path) == null);
            Assert.That(ShardBlobTransfer.HeldBy(path), Is.Null, "let go when the call ended");
        }
        finally
        {
            held.Release();
        }
    }

    [Test]
    public async Task AnUploadTheJobLeftRunningHoldsItsFileUntilItEndsTest()
    {
        // The attempt's time is long; the job is cancelled while the call runs.
        var path = Path.Combine(m_directory, "held.bin");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        var held = new CorpusHeldBlobService(m_blobs);
        using var job = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        try
        {
            Assert.That(async () => await ShardBlobTransfer.UploadAsync(held, path, null, job.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.Multiple(() =>
            {
                Assert.That(held.Uploads, Is.EqualTo(1));
                Assert.That(ShardBlobTransfer.HeldBy(path), Is.Not.Null, "the call still reads the file");
            });

            held.Release();
            await WaitFor(() => ShardBlobTransfer.HeldBy(path) == null);
            Assert.That(ShardBlobTransfer.HeldBy(path), Is.Null, "let go when the call ended");
        }
        finally
        {
            held.Release();
        }
    }

    [Test]
    public async Task ACancelledJobLeavesATransferThatNeverReturnsTest()
    {
        // The attempt's time is long; only the job's token ends the wait.
        var blobId = await UploadAsync();
        CloseTheGate(blobId);

        var hanging = new CorpusHangingBlobService(m_blobs, hangs: 1);
        using var job = new CancellationTokenSource();
        var stuck = ShardBlobTransfer.DownloadAsync(hanging, blobId, 0, null, job.Token);
        var other = ShardBlobTransfer.DownloadAsync(m_blobs, blobId, 0, null, CancellationToken.None);

        await Task.Delay(200);
        Assert.That(other.IsCompleted, Is.False, "the gate is closed and held by the call that never returns");

        job.Cancel();

        Assert.That(async () => await stuck, Throws.InstanceOf<OperationCanceledException>());
        var finished = await Task.WhenAny(other, Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.That(finished, Is.SameAs(other), "the cancelled job no longer holds the gate");
    }

    [Test]
    public void AnAttemptHasTwoMinutesAndTheTimeOfItsBytesAt32KiBASecondTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ShardBlobTransfer.TRANSFER_BASE_TIME, Is.EqualTo(TimeSpan.FromMinutes(2)));
            Assert.That(ShardBlobTransfer.TransferTimeout(0), Is.EqualTo(TimeSpan.FromMinutes(2)));
            Assert.That(ShardBlobTransfer.TransferTimeout(32 * 1024 * 60), Is.EqualTo(TimeSpan.FromMinutes(3)));
            Assert.That(ShardBlobTransfer.TransferTimeout(-5), Is.EqualTo(TimeSpan.FromMinutes(2)));
        });
    }

    #endregion

    #region Tools

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var waited = 0; waited < 200 && !condition(); waited++)
            await Task.Delay(50);
    }

    private async Task<Guid> UploadAsync()
    {
        var path = Path.Combine(m_directory, "blob.bin");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        return await m_blobs.UploadFileAsync(path);
    }

    /// <summary>One failed transfer: the gate lets the next ones through one at a time.</summary>
    private void CloseTheGate(Guid blobId)
    {
        var failing = new CorpusFlakyBlobService(m_blobs, downloadFailures: 1);
        Assert.That(async () => await ShardBlobTransfer.DownloadAsync(failing, blobId, 0, null, CancellationToken.None), Throws.InstanceOf<IOException>());
    }

    #endregion
}
