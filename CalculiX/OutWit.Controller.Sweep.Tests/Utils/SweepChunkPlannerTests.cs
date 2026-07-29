using OutWit.Controller.Sweep.Utils;

namespace OutWit.Controller.Sweep.Tests.Utils;

[TestFixture]
public class SweepChunkPlannerTests
{
    #region Schedule Tests

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
