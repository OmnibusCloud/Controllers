using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Delivery;

/// <summary>
/// A transfer whose attempt does not end in its time, on a blob channel whose calls end when the
/// test says so (<see cref="SmtHeldBlobService"/>). The channel's calls take no token: an attempt
/// that is given up goes on. While it does, no second attempt of the same transfer starts beside
/// it (two downloads into one file of the cache, two uploads of one file of rows), and what it
/// comes to late is the transfer's result; a download keeps its slot among the node's downloads
/// at once for as long as an attempt of it runs; and an attempt that never ends holds nothing for
/// good, since each of these waits has an end of its own.
/// <para>
/// No test here waits for a time to pass before it looks: it waits for the transfer to say that
/// an attempt did not go well (a line of its log), for a call to start, or for the transfer to
/// end. The short time is the time an attempt has before it is given up, or the time a given-up
/// attempt is still waited for; the long one is never reached, and the minute every wait of a
/// test has is there so that a failure is a failed test and not a run that stands still.
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class SmtPackageTransferTests
{
    #region Constants

    private const int SHORT_MS = 50;
    private const int LONG_MS = 600_000;

    private const string PATH = "the local path of the blob";

    private static readonly TimeSpan WAIT = TimeSpan.FromMinutes(1);

    #endregion

    #region Fields

    private readonly List<SmtHeldBlobService> m_held = [];

    private SmtTestLogger m_logger = null!;

    private SmtTaskPackageData m_package = null!;

    private string m_file = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_held.Clear();
        m_logger = new SmtTestLogger();
        m_package = new SmtTaskPackageData { ProblemsBlobId = Guid.NewGuid(), SizeBytes = 1 };
        m_file = Path.Combine(Path.GetTempPath(), $"witai_smt_transfer_{Guid.NewGuid():N}.rows");
        File.WriteAllBytes(m_file, [1, 2, 3]);
    }

    [TearDown]
    public async Task TearDown()
    {
        // No call is left behind, and the node's slots for downloads are all free for the next
        // test: as many downloads as the node has at once start side by side, which they can only
        // do when every slot is free.
        foreach (var held in m_held.ToList())
            held.FailEveryDownload();

        await OpenAsync();
        var all = Held();
        var fetches = Enumerable.Range(0, SmtTransferRig.FetchDegree).Select(_ => SmtTransferRig.FetchAsync(all, m_package, m_logger, CancellationToken.None, LONG_MS, LONG_MS)).ToList();
        await all.Started(SmtTransferRig.FetchDegree).WaitAsync(WAIT);
        for (var call = 0; call < SmtTransferRig.FetchDegree; call++)
            all.EndDownload(call, PATH);

        await Task.WhenAll(fetches).WaitAsync(WAIT);
        File.Delete(m_file);
    }

    #endregion

    #region In Time Tests

    [Test]
    public async Task ADownloadThatEndsInItsTimeIsTakenAndNothingIsSaidTest()
    {
        var blobs = Held();
        blobs.EndDownloadsFrom(0, PATH);

        var (path, fault) = await SmtTransferRig.FetchAsync(blobs, m_package, m_logger, CancellationToken.None, LONG_MS, LONG_MS, 0).WaitAsync(WAIT);

        Assert.Multiple(() =>
        {
            Assert.That(fault, Is.Null);
            Assert.That(path, Is.EqualTo(PATH));
            Assert.That(blobs.Downloads, Is.EqualTo(1));
            Assert.That(m_logger.Lines, Is.Empty, "a download that goes well leaves no line in the node's log");
        });
    }

    [Test]
    public async Task ADownloadThatFailsInItsTimeIsFollowedByTheNextAttemptWithoutTheLateWaitTest()
    {
        var blobs = Held();
        var fetch = SmtTransferRig.FetchAsync(blobs, m_package, m_logger, CancellationToken.None, LONG_MS, LONG_MS, 0);
        await blobs.Started(1).WaitAsync(WAIT);

        blobs.EndDownloadsFrom(1, PATH);
        blobs.FailDownload(0);
        var (path, fault) = await fetch.WaitAsync(WAIT);

        var lines = m_logger.Lines.Select(line => line.Text).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(fault, Is.Null);
            Assert.That(path, Is.EqualTo(PATH));
            Assert.That(blobs.Downloads, Is.EqualTo(2));
            Assert.That(lines, Has.Count.EqualTo(1), "one line: the attempt that failed");
            Assert.That(lines, Has.One.Contains("failed (attempt 1 of 2)"));
        });
    }

    #endregion

    #region Late Attempt Tests

    [Test]
    public async Task ADownloadThatEndsLateIsTheDownloadAndNoSecondOneStartsBesideItTest()
    {
        var blobs = Held();
        var givenUp = m_logger.NextWarning();

        var fetch = SmtTransferRig.FetchAsync(blobs, m_package, m_logger, CancellationToken.None, SHORT_MS, LONG_MS, 0);
        await givenUp.WaitAsync(WAIT);
        blobs.EndDownload(0, PATH);
        var (path, fault) = await fetch.WaitAsync(WAIT);

        Assert.Multiple(() =>
        {
            Assert.That(fault, Is.Null);
            Assert.That(path, Is.EqualTo(PATH), "what the attempt came to after its time is the download");
            Assert.That(blobs.Downloads, Is.EqualTo(1), "no second download of the blob was started beside the one that still ran");
        });
    }

    [Test]
    public async Task AnUploadThatEndsLateIsTheUploadAndNoSecondOneStartsBesideItTest()
    {
        var blobs = Held();
        var blob = Guid.NewGuid();
        var givenUp = m_logger.NextWarning();

        var upload = SmtTransferRig.UploadAsync(blobs, m_file, m_logger, SHORT_MS, LONG_MS, 0);
        await givenUp.WaitAsync(WAIT);
        blobs.EndUpload(0, blob);
        var (uploaded, fault) = await upload.WaitAsync(WAIT);

        Assert.Multiple(() =>
        {
            Assert.That(fault, Is.Null);
            Assert.That(uploaded, Is.EqualTo(blob), "the blob the late upload made is the one the node returns");
            Assert.That(blobs.Uploads, Is.EqualTo(1), "the rows were not uploaded a second time, which would leave a blob nobody names");
        });
    }

    [Test]
    public async Task ADownloadThatFailsLateIsFollowedByTheNextAttemptTest()
    {
        var blobs = Held();
        blobs.EndDownloadsFrom(1, PATH);
        var givenUp = m_logger.NextWarning();

        var fetch = SmtTransferRig.FetchAsync(blobs, m_package, m_logger, CancellationToken.None, SHORT_MS, LONG_MS, 0);
        await givenUp.WaitAsync(WAIT);
        blobs.FailDownload(0);
        var (path, fault) = await fetch.WaitAsync(WAIT);

        Assert.Multiple(() =>
        {
            Assert.That(fault, Is.Null);
            Assert.That(path, Is.EqualTo(PATH));
            Assert.That(blobs.Downloads, Is.EqualTo(2), "the next attempt started when the first had ended");
        });
    }

    [Test]
    public async Task ADownloadThatNeverEndsIsPassedByTheNextAttemptAfterItsLateTimeTest()
    {
        var blobs = Held();
        blobs.EndDownloadsFrom(1, PATH);

        var (path, fault) = await SmtTransferRig.FetchAsync(blobs, m_package, m_logger, CancellationToken.None, SHORT_MS, SHORT_MS, 0).WaitAsync(WAIT);

        Assert.Multiple(() =>
        {
            Assert.That(fault, Is.Null);
            Assert.That(path, Is.EqualTo(PATH), "a dead attempt does not hold the transfer for good");
            Assert.That(blobs.Downloads, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ADownloadWhoseEveryAttemptRunsOutOfItsTimeFailsWithItsCodeTest()
    {
        var blobs = Held();

        var (path, fault) = await SmtTransferRig.FetchAsync(blobs, m_package, m_logger, CancellationToken.None, SHORT_MS, SHORT_MS, 0, 0).WaitAsync(WAIT);

        Assert.Multiple(() =>
        {
            Assert.That(path, Is.Null);
            Assert.That(fault, Is.EqualTo(SmtNodeFaults.DOWNLOAD_FAILED));
            Assert.That(blobs.Downloads, Is.EqualTo(3), "an attempt for each try");
        });
    }

    #endregion

    #region Slot Tests

    [Test]
    public async Task ADownloadThatWasGivenUpKeepsItsSlotWhileItRunsTest()
    {
        // As many downloads as a node has at once, all in flight, of a job that is then
        // cancelled. Their calls go on: nothing can stop them. A download of another job does
        // not start beside them, and starts when one of them has ended.
        await OpenAsync();
        using var job = new CancellationTokenSource();
        var stuck = Held();
        var fetches = Enumerable.Range(0, SmtTransferRig.FetchDegree).Select(_ => SmtTransferRig.FetchAsync(stuck, m_package, m_logger, job.Token, LONG_MS, LONG_MS)).ToList();
        await stuck.Started(SmtTransferRig.FetchDegree).WaitAsync(WAIT);

        await job.CancelAsync();
        foreach (var fetch in fetches)
            Assert.That(async () => await fetch.WaitAsync(WAIT), Throws.InstanceOf<OperationCanceledException>());

        var other = Held();
        other.EndDownloadsFrom(0, PATH);
        var next = SmtTransferRig.FetchAsync(other, m_package, m_logger, CancellationToken.None, LONG_MS, LONG_MS);
        var startedBeside = other.Downloads;

        stuck.FailDownload(2);
        var (path, fault) = await next.WaitAsync(WAIT);

        Assert.Multiple(() =>
        {
            Assert.That(startedBeside, Is.Zero, "no download starts while as many as a node has at once are still in flight");
            Assert.That(fault, Is.Null);
            Assert.That(path, Is.EqualTo(PATH), "it starts when one of them has ended");
            Assert.That(other.Downloads, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ADownloadCancelledWhileItsLateAttemptIsWaitedForKeepsItsSlotTest()
    {
        // The same, with downloads that ran out of their time first: each is in the wait for
        // its late attempt when the job is cancelled. No second attempt of any was started.
        await OpenAsync();
        using var job = new CancellationTokenSource();
        var stuck = Held();
        var givenUp = m_logger.Warnings(SmtTransferRig.FetchDegree);
        var fetches = Enumerable.Range(0, SmtTransferRig.FetchDegree).Select(_ => SmtTransferRig.FetchAsync(stuck, m_package, m_logger, job.Token, SHORT_MS, LONG_MS, 0)).ToList();
        await givenUp.WaitAsync(WAIT);

        await job.CancelAsync();
        foreach (var fetch in fetches)
            Assert.That(async () => await fetch.WaitAsync(WAIT), Throws.InstanceOf<OperationCanceledException>());

        var other = Held();
        other.EndDownloadsFrom(0, PATH);
        var next = SmtTransferRig.FetchAsync(other, m_package, m_logger, CancellationToken.None, LONG_MS, LONG_MS);
        var startedBeside = other.Downloads;

        stuck.EndDownload(1, PATH);
        var (path, fault) = await next.WaitAsync(WAIT);

        Assert.Multiple(() =>
        {
            Assert.That(stuck.Downloads, Is.EqualTo(SmtTransferRig.FetchDegree), "no second attempt beside one that still ran");
            Assert.That(startedBeside, Is.Zero, "no download starts while as many as a node has at once are still in flight");
            Assert.That(fault, Is.Null);
            Assert.That(path, Is.EqualTo(PATH), "it starts when one of them has ended");
        });
    }

    [Test]
    public async Task ASlotKeptForADownloadThatNeverEndsGoesBackAfterTheLateTimeTest()
    {
        // The same, with downloads that never end: their slots go back when the time a given-up
        // attempt is waited for has passed, so that a dead channel does not stop every download
        // of the node for the life of its process.
        await OpenAsync();
        using var job = new CancellationTokenSource();
        var stuck = Held();
        var fetches = Enumerable.Range(0, SmtTransferRig.FetchDegree).Select(_ => SmtTransferRig.FetchAsync(stuck, m_package, m_logger, job.Token, LONG_MS, SHORT_MS)).ToList();
        await stuck.Started(SmtTransferRig.FetchDegree).WaitAsync(WAIT);

        await job.CancelAsync();
        foreach (var fetch in fetches)
            Assert.That(async () => await fetch.WaitAsync(WAIT), Throws.InstanceOf<OperationCanceledException>());

        var other = Held();
        other.EndDownloadsFrom(0, PATH);
        var (path, fault) = await SmtTransferRig.FetchAsync(other, m_package, m_logger, CancellationToken.None, LONG_MS, LONG_MS).WaitAsync(WAIT);

        Assert.Multiple(() =>
        {
            Assert.That(fault, Is.Null);
            Assert.That(path, Is.EqualTo(PATH), "the download ran although not one of the given-up ones had ended");
            Assert.That(stuck.Downloads, Is.EqualTo(SmtTransferRig.FetchDegree));
        });
    }

    #endregion

    #region Tools

    private SmtHeldBlobService Held()
    {
        var blobs = new SmtHeldBlobService();
        m_held.Add(blobs);
        return blobs;
    }

    /// <summary>
    /// A download that goes well at once: after one, the node's transfers run side by side (the
    /// first transfer over its blob channel goes alone, and so does every one after a failure).
    /// </summary>
    private async Task OpenAsync()
    {
        var blobs = Held();
        blobs.EndDownloadsFrom(0, PATH);
        var (path, _) = await SmtTransferRig.FetchAsync(blobs, m_package, m_logger, CancellationToken.None, LONG_MS, LONG_MS).WaitAsync(WAIT);
        Assert.That(path, Is.EqualTo(PATH));
    }

    #endregion
}
