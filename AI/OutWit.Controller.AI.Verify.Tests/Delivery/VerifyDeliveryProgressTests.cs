using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Delivery;

namespace OutWit.Controller.AI.Verify.Tests.Delivery;

/// <summary>
/// What a node tells the host about its share: the delivery first (packages and megabytes
/// received), then the work, and a fraction that is the share's, not one package's.
/// </summary>
[TestFixture]
public sealed class VerifyDeliveryProgressTests
{
    #region Tests

    [Test]
    public void TheShareIsReportedAsDeliveryThenWorkTest()
    {
        var reports = new List<(double Fraction, string Stage)>();
        var jobId = Guid.NewGuid();
        var packages = new[] { Package(tasks: 100, megabytes: 2), Package(tasks: 300, megabytes: 6) };

        // A node starts every package of its share together.
        var progress = VerifyDeliveryProgress.Enter(jobId, packages[0], (fraction, stage) => reports.Add((fraction, stage)), NullLogger.Instance);
        VerifyDeliveryProgress.Enter(jobId, packages[1], (fraction, stage) => reports.Add((fraction, stage)), NullLogger.Instance);

        Assert.That(reports[0].Stage, Is.EqualTo("receiving tasks: 0/1 packages, 0.0/2.0 MB; 0/100 tasks done"));

        progress.Received(packages[0]);
        progress.Received(packages[1]);

        // The report that ends the delivery always reaches the host.
        Assert.That(reports[^1], Is.EqualTo((0.0, "0/400 tasks")));

        for (var i = 0; i < 100; i++)
            progress.TaskDone();

        Thread.Sleep(600);
        progress.TaskDone();

        Assert.Multiple(() =>
        {
            Assert.That(reports[^1].Stage, Is.EqualTo("101/400 tasks"));
            Assert.That(reports[^1].Fraction, Is.EqualTo(101.0 / 400).Within(1e-9), "the fraction of the node's share, not of one package");
        });

        progress.Leave();
        progress.Leave();
    }

    [Test]
    public void ADeliveryInProgressSaysHowMuchHasArrivedTest()
    {
        var reports = new List<string>();
        var jobId = Guid.NewGuid();
        var packages = new[] { Package(tasks: 10, megabytes: 1), Package(tasks: 10, megabytes: 1), Package(tasks: 10, megabytes: 1) };

        VerifyDeliveryProgress progress = null!;
        foreach (var package in packages)
            progress = VerifyDeliveryProgress.Enter(jobId, package, (_, stage) => reports.Add(stage), NullLogger.Instance);

        progress.Received(packages[0]);
        Thread.Sleep(600);
        progress.Received(packages[1]);

        Assert.That(reports[^1], Is.EqualTo("receiving tasks: 2/3 packages, 2.0/3.0 MB; 0/30 tasks done"));

        progress.Received(packages[2]);
        foreach (var _ in packages)
            progress.Leave();
    }

    [Test]
    public void AJobStartsAfreshOnceItsShareIsOverTest()
    {
        var reports = new List<string>();
        var jobId = Guid.NewGuid();
        var package = Package(tasks: 5, megabytes: 1);

        var first = VerifyDeliveryProgress.Enter(jobId, package, (_, stage) => reports.Add(stage), NullLogger.Instance);
        first.Received(package);
        first.Leave();

        var second = VerifyDeliveryProgress.Enter(jobId, package, (_, stage) => reports.Add(stage), NullLogger.Instance);
        second.Received(package);
        second.Leave();

        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(reports[^1], Is.EqualTo("0/5 tasks"), "the second share does not carry the first one's counts");
    }

    [Test]
    public void AHostWithoutAProgressSinkIsNotReportedToTest()
    {
        var package = Package(tasks: 5, megabytes: 1);

        var progress = VerifyDeliveryProgress.Enter(Guid.NewGuid(), package, null, NullLogger.Instance);

        Assert.That(() =>
        {
            progress.Received(package);
            progress.TaskDone();
            progress.Leave();
        }, Throws.Nothing);
    }

    #endregion

    #region Tools

    private static VerifyTaskPackageData Package(int tasks, int megabytes)
    {
        return new VerifyTaskPackageData
        {
            TasksBlobId = Guid.NewGuid(),
            TaskCount = tasks,
            BatchCount = 1,
            SizeBytes = megabytes * 1024L * 1024L
        };
    }

    #endregion
}
