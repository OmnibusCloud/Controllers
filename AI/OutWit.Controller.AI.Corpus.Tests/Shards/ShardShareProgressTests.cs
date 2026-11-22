using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.AI.Corpus.Shards;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// What a node tells the host about its share of shards: the delivery first (shards and
/// megabytes received from the origin), then the work, with a fraction that is the share's.
/// </summary>
[TestFixture]
public sealed class ShardShareProgressTests
{
    #region Constants

    private const long MEGABYTE = 1024L * 1024L;

    #endregion

    #region Tests

    [Test]
    public void TheShareIsReportedAsDeliveryThenWorkTest()
    {
        var reports = new List<(double Fraction, string Stage)>();
        var jobId = Guid.NewGuid();

        // A node starts every shard of its share together.
        var first = ShardShareProgress.Enter(jobId, 60 * MEGABYTE, (fraction, stage) => reports.Add((fraction, stage)), NullLogger.Instance);
        var second = ShardShareProgress.Enter(jobId, 40 * MEGABYTE, (fraction, stage) => reports.Add((fraction, stage)), NullLogger.Instance);

        Assert.That(reports[0].Stage, Is.EqualTo("receiving shards: 0/1, 0.0/60.0 MB; 0/1 processed"));

        first.Received(30 * MEGABYTE);
        first.Received(60 * MEGABYTE);
        first.Fetched();

        Thread.Sleep(600);
        second.Received(20 * MEGABYTE);

        Assert.Multiple(() =>
        {
            Assert.That(reports[^1].Stage, Is.EqualTo("receiving shards: 1/2, 80.0/100.0 MB; 0/2 processed"));
            Assert.That(reports[^1].Fraction, Is.EqualTo(0.2 * 0.8).Within(1e-9), "delivery is a fifth of a share's progress");
        });

        second.Received(40 * MEGABYTE);
        second.Fetched();

        // The report that ends the delivery always reaches the host.
        Assert.That(reports[^1], Is.EqualTo((0.2, "0/2 shards processed")));

        first.Dispose();

        Assert.That(reports[^1].Stage, Is.EqualTo("1/2 shards processed"));
        Assert.That(reports[^1].Fraction, Is.EqualTo(0.2 + 0.8 * 0.5).Within(1e-9));

        second.Dispose();

        Assert.That(reports[^1], Is.EqualTo((1.0, "2/2 shards processed")));
    }

    [Test]
    public void AFetchThatStartsOverTakesItsBytesBackTest()
    {
        var reports = new List<string>();
        var ticket = ShardShareProgress.Enter(Guid.NewGuid(), 10 * MEGABYTE, (_, stage) => reports.Add(stage), NullLogger.Instance);

        ticket.Received(8 * MEGABYTE);
        ticket.Received(0);
        Thread.Sleep(600);
        ticket.Received(2 * MEGABYTE);

        Assert.That(reports[^1], Is.EqualTo("receiving shards: 0/1, 2.0/10.0 MB; 0/1 processed"));

        ticket.Dispose();
    }

    [Test]
    public void AShardThatNeverFetchedStillClosesItsDeliveryTest()
    {
        var reports = new List<(double Fraction, string Stage)>();

        // Refused before the fetch: the ticket is disposed without Fetched.
        var ticket = ShardShareProgress.Enter(Guid.NewGuid(), 0, (fraction, stage) => reports.Add((fraction, stage)), NullLogger.Instance);
        ticket.Dispose();
        ticket.Dispose();

        Assert.That(reports[^1], Is.EqualTo((1.0, "1/1 shards processed")));
    }

    [Test]
    public void WithoutSizesTheDeliveryIsCountedInShardsTest()
    {
        var reports = new List<(double Fraction, string Stage)>();
        var jobId = Guid.NewGuid();

        var first = ShardShareProgress.Enter(jobId, 0, (fraction, stage) => reports.Add((fraction, stage)), NullLogger.Instance);
        var second = ShardShareProgress.Enter(jobId, 0, (fraction, stage) => reports.Add((fraction, stage)), NullLogger.Instance);

        first.Received(5 * MEGABYTE);
        first.Fetched();
        Thread.Sleep(600);
        second.Received(MEGABYTE);

        Assert.Multiple(() =>
        {
            Assert.That(reports[^1].Stage, Is.EqualTo("receiving shards: 1/2, 6.0 MB; 0/2 processed"));
            Assert.That(reports[^1].Fraction, Is.EqualTo(0.2 * 0.5).Within(1e-9));
        });

        first.Dispose();
        second.Dispose();
    }

    [Test]
    public void AHostWithoutAProgressSinkIsNotReportedToTest()
    {
        var ticket = ShardShareProgress.Enter(Guid.NewGuid(), MEGABYTE, null, NullLogger.Instance);

        Assert.That(() =>
        {
            ticket.Received(MEGABYTE);
            ticket.Fetched();
            ticket.Dispose();
        }, Throws.Nothing);
    }

    #endregion
}
