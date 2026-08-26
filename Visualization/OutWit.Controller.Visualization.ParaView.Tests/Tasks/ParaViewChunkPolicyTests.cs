using OutWit.Controller.Visualization.ParaView.Tasks;
using OutWit.Controller.Visualization.ParaView.Validation;

namespace OutWit.Controller.Visualization.ParaView.Tests.Tasks;

/// <summary>
/// The FrameBatch chunk sizing (docs 03, section 27, item 2): a small job still splits per output,
/// a long animation batches up to the cap, and the union of a chunk never exceeds what one task may
/// materialize.
/// </summary>
[TestFixture]
public sealed class ParaViewChunkPolicyTests
{
    #region Tests

    [TestCase(0, 1)]
    [TestCase(1, 1)]
    [TestCase(5, 1)]
    [TestCase(24, 1)]
    [TestCase(25, 2)]
    [TestCase(60, 3)]
    [TestCase(72, 3)]
    [TestCase(360, 15)]
    [TestCase(768, 32)]
    [TestCase(10_000, 32)]
    public void ChunkSizeIsTheCeilingOverTheTargetClampedToTheCapTest(int outputs, int expected)
    {
        Assert.That(ParaViewChunkPolicy.ComputeChunkSize(outputs), Is.EqualTo(expected));
    }

    [Test]
    public void ChunkSizeNeverExceedsTheCapNorFallsBelowOneTest()
    {
        for (var outputs = 1; outputs <= 20_000; outputs += 7)
        {
            var chunk = ParaViewChunkPolicy.ComputeChunkSize(outputs);
            Assert.That(chunk, Is.InRange(1, ParaViewChunkPolicy.MAX_CHUNK), $"{outputs} outputs");
            Assert.That((long)chunk * ParaViewChunkPolicy.TARGET_CHUNKS, Is.GreaterThanOrEqualTo(Math.Min(outputs, ParaViewChunkPolicy.MAX_CHUNK * ParaViewChunkPolicy.TARGET_CHUNKS)), $"{outputs} outputs fit the target chunk count until the cap binds");
        }
    }

    [Test]
    public void SubsetLimitGuardsTheUnionTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ParaViewChunkPolicy.ExceedsSubsetLimit(0, ParaViewInputLimits.MAX_TASK_SUBSET_BYTES), Is.False);
            Assert.That(ParaViewChunkPolicy.ExceedsSubsetLimit(1, ParaViewInputLimits.MAX_TASK_SUBSET_BYTES), Is.True);
            Assert.That(ParaViewChunkPolicy.ExceedsSubsetLimit(ParaViewInputLimits.MAX_TASK_SUBSET_BYTES / 2, ParaViewInputLimits.MAX_TASK_SUBSET_BYTES / 2), Is.False);
        });
    }

    #endregion
}
