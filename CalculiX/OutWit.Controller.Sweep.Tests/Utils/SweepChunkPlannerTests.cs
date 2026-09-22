using OutWit.Controller.Sweep.Utils;

namespace OutWit.Controller.Sweep.Tests.Utils;

[TestFixture]
public class SweepChunkPlannerTests
{
    #region Schedule Tests

    [Test]
    public void TheFleetWidthRaisesANarrowFirstChunkTest()
    {
        // Six machines, a client asking for a first chunk of two: the first wave would idle
        // four of them, so the plan opens at six and grows from there.
        var sizes = SweepChunkPlanner.Sizes(firstChunkSize: 2, maxChunkSize: 48, totalVariants: 12, availableNodes: 6);

        Assert.That(sizes, Is.EqualTo(new[] { 6, 6 }));
    }

    [Test]
    public void AWiderClientChunkStaysAndTheCapRisesWithTheWidthTest()
    {
        var wide = SweepChunkPlanner.Sizes(firstChunkSize: 10, maxChunkSize: 48, totalVariants: 30, availableNodes: 6);
        Assert.That(wide, Is.EqualTo(new[] { 10, 20 }));

        // The harness asked for chunks of at most three; on a six-machine fleet the cap follows the width.
        var capped = SweepChunkPlanner.Sizes(firstChunkSize: 2, maxChunkSize: 3, totalVariants: 12, availableNodes: 6);
        Assert.That(capped, Is.EqualTo(new[] { 6, 6 }));
    }

    [Test]
    public void AnUnknownFleetLeavesThePlanAsAskedTest()
    {
        Assert.That(SweepChunkPlanner.Sizes(2, 3, 12, availableNodes: 0), Is.EqualTo(SweepChunkPlanner.Sizes(2, 3, 12)));
        Assert.That(SweepChunkPlanner.Sizes(2, 3, 12, availableNodes: -1), Is.EqualTo(SweepChunkPlanner.Sizes(2, 3, 12)));
    }

    [Test]
    public void WidenedSizesStillSumToTheVariantCountTest()
    {
        foreach (var total in new[] { 1, 5, 12, 100 })
        foreach (var nodes in new[] { 1, 6, 40 })
            Assert.That(SweepChunkPlanner.Sizes(2, 48, total, nodes).Sum(), Is.EqualTo(total), $"total {total}, nodes {nodes}");
    }

    [Test]
    public void SizesGrowGeometricallyToTheCapTest()
    {
        var sizes = SweepChunkPlanner.Sizes(firstChunkSize: 10, maxChunkSize: 80, totalVariants: 300);

        Assert.That(sizes, Is.EqualTo(new[] { 10, 20, 40, 80, 80, 70 }));
    }

    [Test]
    public void SizesAlwaysSumToTheVariantCountTest()
    {
        foreach (var total in new[] { 1, 5, 8, 48, 100, 300, 1000 })
        {
            var sizes = SweepChunkPlanner.Sizes(0, 0, total);

            Assert.That(sizes.Sum(), Is.EqualTo(total), $"total={total}");
            Assert.That(sizes, Is.All.GreaterThan(0), $"total={total}");
        }
    }

    [Test]
    public void DefaultsApplyWhenClientPassesZeroTest()
    {
        var sizes = SweepChunkPlanner.Sizes(0, 0, 100);

        Assert.That(sizes[0], Is.EqualTo(SweepChunkPlanner.DEFAULT_FIRST_CHUNK_SIZE));
        Assert.That(sizes.Max(), Is.LessThanOrEqualTo(SweepChunkPlanner.DEFAULT_MAX_CHUNK_SIZE));
    }

    [Test]
    public void SmallSweepIsOneChunkTest()
    {
        var sizes = SweepChunkPlanner.Sizes(8, 48, 5);

        Assert.That(sizes, Is.EqualTo(new[] { 5 }));
    }

    [Test]
    public void CapBelowFirstIsRaisedToFirstTest()
    {
        var sizes = SweepChunkPlanner.Sizes(firstChunkSize: 10, maxChunkSize: 4, totalVariants: 30);

        Assert.That(sizes, Is.EqualTo(new[] { 10, 10, 10 }));
    }

    [Test]
    public void EmptySweepIsRejectedTest()
    {
        Assert.That(() => SweepChunkPlanner.Sizes(8, 48, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    #endregion
}
