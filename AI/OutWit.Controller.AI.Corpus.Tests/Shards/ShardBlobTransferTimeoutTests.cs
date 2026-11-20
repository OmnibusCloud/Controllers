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
    public async Task ATransferThatNeverReturnsHoldsNoOtherJobWhenItsTimeIsUpTest()
    {
        ShardBlobTransfer.BaseTime = TimeSpan.FromMilliseconds(300);
        var blobId = await UploadAsync();
        CloseTheGate(blobId);

        using var job = new CancellationTokenSource();
        var hanging = new CorpusHangingBlobService(m_blobs, hangs: 1);
        var stuck = ShardBlobTransfer.DownloadAsync(hanging, blobId, 0, null, job.Token);
        var other = ShardBlobTransfer.DownloadAsync(m_blobs, blobId, 0, null, CancellationToken.None);

        var finished = await Task.WhenAny(other, Task.Delay(TimeSpan.FromSeconds(15)));

        Assert.Multiple(() =>
        {
            Assert.That(hanging.Hung, Is.EqualTo(1), "the first call was taken and never answered, and not called again");
            Assert.That(finished, Is.SameAs(other), "another job's transfer went through: the gate is free");
            Assert.That(stuck.IsCompleted, Is.False, "its own transfer waits for the call it gave up, and starts no second one beside it");
        });

        job.Cancel();
        Assert.That(async () => await stuck, Throws.InstanceOf<OperationCanceledException>(), "the job's token ends that wait");
    }

    [Test]
    public async Task AnAttemptThatRanOutOfItsTimeIsWaitedForAndNoSecondOneStartsBesideItTest()
    {
        // An upload that takes longer than its attempt's time and then succeeds: retried at once,
        // the same file went up twice side by side (nine times with every retry), each a blob.
        ShardBlobTransfer.BaseTime = TimeSpan.FromMilliseconds(200);
        ShardBlobTransfer.Delays = [TimeSpan.Zero, TimeSpan.Zero];
        var path = Path.Combine(m_directory, "product.bin");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        var slow = new CorpusSlowBlobService(m_blobs, TimeSpan.FromMilliseconds(900));

        var blobId = await ShardBlobTransfer.UploadAsync(slow, path, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(slow.Uploads, Is.EqualTo(1), "one call for the file: the one that ran out of its time was waited for");
            Assert.That(blobId, Is.EqualTo(slow.First), "and its answer is the transfer's");
            Assert.That(ShardBlobTransfer.HeldBy(path), Is.Null);
        });
    }

    [Test]
    public async Task AnUploadTheJobLeftRunningHoldsItsFileUntilItEndsTest()
    {
        ShardBlobTransfer.BaseTime = TimeSpan.FromMilliseconds(200);
        var path = Path.Combine(m_directory, "held.bin");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        var hanging = new CorpusHangingBlobService(m_blobs, hangs: 1);
        using var job = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));

        Assert.That(async () => await ShardBlobTransfer.UploadAsync(hanging, path, null, job.Token), Throws.InstanceOf<OperationCanceledException>());

        Assert.Multiple(() =>
        {
            Assert.That(hanging.Hung, Is.EqualTo(1));
            Assert.That(ShardBlobTransfer.HeldBy(path), Is.Not.Null, "the call still reads the file");
        });
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
