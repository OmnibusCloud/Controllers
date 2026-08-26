using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Tasks;

namespace OutWit.Controller.Visualization.ParaView.Tests.Tasks;

/// <summary>
/// The frame-set completeness check behind Collect and CollectStill (audit wave 2 — the component
/// whose job is refusing a frame set with a hole had no tests): results come back in task-index
/// order, a missing or duplicated index, an empty image, an empty set or two results claiming one
/// identity are refused by name.
/// </summary>
[TestFixture]
public sealed class ParaViewResultOrderingTests
{
    #region Tools

    private static ParaViewRenderResultData Result(int taskIndex, string? taskId = null, Guid? image = null)
    {
        return new ParaViewRenderResultData
        {
            TaskIndex = taskIndex,
            TaskId = taskId ?? $"task-{taskIndex}",
            ImageBlobId = image ?? Guid.NewGuid()
        };
    }

    #endregion

    #region Tests

    [Test]
    public void ResultsAreOrderedByTaskIndexWhateverTheArrivalOrderTest()
    {
        var ordered = ParaViewResultOrdering.Order([Result(2), Result(0), Result(1)], "ParaView.Collect");

        Assert.That(ordered.Select(me => me.TaskIndex), Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public void NullEntriesAreSkippedTest()
    {
        var ordered = ParaViewResultOrdering.Order([Result(1), null, Result(0)], "ParaView.Collect");

        Assert.That(ordered, Has.Count.EqualTo(2));
    }

    [Test]
    public void AHoleInTheIndexSequenceIsRefusedNamingTheMissingIndexTest()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ParaViewResultOrdering.Order([Result(0), Result(2)], "ParaView.Collect"));

        Assert.That(error!.Message, Does.Contain("task index 1 is missing").And.Contain("got 2 result(s)"));
    }

    [Test]
    public void ADuplicatedIndexIsRefusedTest()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ParaViewResultOrdering.Order([Result(0, "a"), Result(0, "b"), Result(1)], "ParaView.Collect"));

        Assert.That(error!.Message, Does.Contain("task index 0 appears more than once"));
    }

    [Test]
    public void ASetNotStartingAtZeroIsRefusedTest()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ParaViewResultOrdering.Order([Result(1), Result(2)], "ParaView.CollectStill"));

        Assert.That(error!.Message, Does.StartWith("ParaView.CollectStill").And.Contain("task index 0 is missing"));
    }

    [Test]
    public void AnEmptySetIsRefusedTest()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ParaViewResultOrdering.Order([null], "ParaView.Collect"));

        Assert.That(error!.Message, Does.Contain("no render results to collect"));
    }

    [Test]
    public void AResultWithoutAnImageBlobIsRefusedTest()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ParaViewResultOrdering.Order([Result(0), Result(1, image: Guid.Empty)], "ParaView.Collect"));

        Assert.That(error!.Message, Does.Contain("task index 1 carries no image blob"));
    }

    [Test]
    public void TwoResultsClaimingOneIdentityAreRefusedTest()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ParaViewResultOrdering.Order([Result(0, "same"), Result(1, "same")], "ParaView.Collect"));

        Assert.That(error!.Message, Does.Contain("two results claim the same task identity"));
    }

    [Test]
    public void EmptyIdentitiesDoNotCountAsDuplicatesTest()
    {
        var ordered = ParaViewResultOrdering.Order([Result(0, ""), Result(1, "")], "ParaView.Collect");

        Assert.That(ordered, Has.Count.EqualTo(2));
    }

    #endregion
}
