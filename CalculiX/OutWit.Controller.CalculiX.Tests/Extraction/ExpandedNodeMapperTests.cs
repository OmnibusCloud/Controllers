using OutWit.Controller.CalculiX.Extraction;

namespace OutWit.Controller.CalculiX.Tests.Extraction;

/// <summary>
/// Deck nodes of expanded elements mapped to the result nodes standing for
/// them: the node at the deck node's own position when there is one, the
/// nearest ones otherwise.
/// </summary>
[TestFixture]
public class ExpandedNodeMapperTests
{
    #region Map Tests

    [Test]
    public void ANodeAtItsOwnPositionIsTheMapTest()
    {
        // An S8R corner node at (0,0,0) expands to bottom, mid and top nodes.
        var mapper = new ExpandedNodeMapper();
        mapper.Add(370, 0.0, 0.0, -1.0);
        mapper.Add(371, 0.0, 0.0, 0.0);
        mapper.Add(372, 0.0, 0.0, 1.0);

        var mapped = mapper.Map([0.0, 0.0, 0.0], out var atPosition);

        Assert.That(mapped, Is.EqualTo(new[] { 371 }));
        Assert.That(atPosition, Is.True);
    }

    [Test]
    public void AMidSideNodeTakesItsTiedNearestNodesTest()
    {
        // A quadratic shell's mid-side node has no mid-surface node: its top
        // and bottom nodes tie at half the thickness.
        var mapper = new ExpandedNodeMapper();
        mapper.Add(370, 0.0, 0.0, -1.0);
        mapper.Add(371, 0.0, 0.0, 0.0);
        mapper.Add(372, 0.0, 0.0, 1.0);
        mapper.Add(373, 2.5, 0.0, -1.0);
        mapper.Add(375, 2.5, 0.0, 1.0);

        var mapped = mapper.Map([2.5, 0.0, 0.0], out var atPosition);

        Assert.That(mapped, Is.EquivalentTo(new[] { 373, 375 }));
        Assert.That(atPosition, Is.False);
    }

    [Test]
    public void APointOutsideTheMeshStillFindsItsNearestNodeTest()
    {
        var mapper = new ExpandedNodeMapper();
        for (var i = 0; i < 1000; i++)
            mapper.Add(1000 + i, i * 0.1, 0.0, 0.0);

        var mapped = mapper.Map([250.0, 40.0, 0.0], out var atPosition);

        Assert.That(mapped, Is.EqualTo(new[] { 1999 }));
        Assert.That(atPosition, Is.False);
    }

    [Test]
    public void AnEmptyMeshMapsNothingTest()
    {
        var mapper = new ExpandedNodeMapper();

        Assert.That(mapper.Map([0.0, 0.0, 0.0], out var atPosition), Is.Empty);
        Assert.That(atPosition, Is.False);
    }

    [Test]
    public void TheToleranceFollowsTheFrdsSixDigitsTest()
    {
        // 1234.56 prints as 1.23456E+03 in the .frd: within tolerance of the
        // deck's 1234.5649, not of 1234.6.
        var mapper = new ExpandedNodeMapper();
        mapper.Add(1, 1234.56, 0.0, 0.0);

        Assert.That(mapper.Map([1234.5649, 0.0, 0.0], out var same), Is.EqualTo(new[] { 1 }));
        Assert.That(same, Is.True);
        mapper.Map([1234.6, 0.0, 0.0], out var other);
        Assert.That(other, Is.False);
    }

    #endregion
}
