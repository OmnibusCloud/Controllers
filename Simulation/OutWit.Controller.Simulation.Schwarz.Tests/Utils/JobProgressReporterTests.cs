using OutWit.Controller.Simulation.Schwarz.Tests.Mock;
using OutWit.Controller.Simulation.Schwarz.Utils;

namespace OutWit.Controller.Simulation.Schwarz.Tests.Utils;

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

        var accepted = JobProgressReporter.Report(manager, jobId, 0.4, "round 12: residual 1.00E-03, target 1.00E-08");

        Assert.That(accepted, Is.True);
        Assert.That(manager.Reports, Is.EqualTo(new[] { (jobId, 0.4, (string?)"round 12: residual 1.00E-03, target 1.00E-08") }));
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
