using OutWit.Controller.AI.Smt.Live.Server;
using OutWit.Controller.AI.Smt.Live.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Live.Tests.Server;

/// <summary>
/// The waiting for a job on a real server, against the SDK's jobs in memory: a connection lost
/// for less than the tolerance is waited out, however many polls that takes; one lost for longer
/// ends the waiting (the job goes on on the server).
/// </summary>
[TestFixture]
public sealed class SmtLiveJobCloudTests
{
    #region Constants

    private static readonly TimeSpan INTERVAL = TimeSpan.FromMilliseconds(1);

    #endregion

    #region Tests

    [Test]
    public async Task AConnectionLostForLessThanTheToleranceIsWaitedOutTest()
    {
        // Twenty failed polls in a row: a few seconds of a real interval, well within the tolerance.
        var jobs = new SmtLiveJobsFake().Running().Failing(20).Completed();
        using var output = new StringWriter();
        var job = new SmtLiveJobCloud(jobs, Guid.NewGuid(), "SmtRun", INTERVAL, TimeSpan.FromMinutes(1), output);

        var end = await job.WaitAsync(null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(end.Completed, Is.True);
            Assert.That(jobs.Reads, Is.EqualTo(22));
            Assert.That(output.ToString(), Does.Contain("the job's status could not be read"));
        });
    }

    [Test]
    public void AConnectionLostForLongerThanTheToleranceEndsTheWaitingTest()
    {
        var jobs = new SmtLiveJobsFake().Running().Failing(1);
        using var output = new StringWriter();
        var job = new SmtLiveJobCloud(jobs, Guid.NewGuid(), "SmtRun", INTERVAL, TimeSpan.FromMilliseconds(200), output);

        Assert.ThrowsAsync<IOException>(() => job.WaitAsync(null, CancellationToken.None));
        Assert.That(jobs.Reads, Is.GreaterThan(2), "the status was asked for again within the tolerance");
    }

    #endregion
}
