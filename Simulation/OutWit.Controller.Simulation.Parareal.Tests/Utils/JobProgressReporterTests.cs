using OutWit.Controller.Simulation.Parareal.Tests.Mock;
using OutWit.Controller.Simulation.Parareal.Utils;

namespace OutWit.Controller.Simulation.Parareal.Tests.Utils;

/// <summary>
/// The reporter reaches the host's job-level sink by reflection, so a host without it (an older
/// server, the SDK) must be a silent no-op rather than a failed solve.
/// </summary>
[TestFixture]
public class JobProgressReporterTests
{
    #region Reporter Tests

    [Test]
    public void ReportReachesTheHostSinkTest()
    {
        var manager = new JobProgressRecordingManager();
        var jobId = Guid.NewGuid();

        var accepted = JobProgressReporter.Report(manager, jobId, 0.4, "iteration 2: correction 3.11E-01, target 3.00E-04");

        Assert.That(accepted, Is.True);
        Assert.That(manager.Reports, Is.EqualTo(new[] { (jobId, 0.4, (string?)"iteration 2: correction 3.11E-01, target 3.00E-04") }));
    }

    [Test]
    public void HostWithoutTheSinkIsSkippedTest()
    {
        Assert.That(JobProgressReporter.Report(new ProcessingManagerWithoutJobProgress(), Guid.NewGuid(), 0.4, null), Is.False);
        Assert.That(JobProgressReporter.Report(null, Guid.NewGuid(), 0.4, null), Is.False);
    }

    [Test]
    public void ThrowingSinkDoesNotEscapeTest()
    {
        var manager = new JobProgressRecordingManager { Failure = new InvalidOperationException("sink failure") };

        Assert.That(JobProgressReporter.Report(manager, Guid.NewGuid(), 0.4, null), Is.False);
    }

    #endregion
}
