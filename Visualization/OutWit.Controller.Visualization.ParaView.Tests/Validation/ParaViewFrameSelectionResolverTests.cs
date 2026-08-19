using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Validation;

namespace OutWit.Controller.Visualization.ParaView.Tests.Validation;

[TestFixture]
public sealed class ParaViewFrameSelectionResolverTests
{
    [Test]
    public void SingleResolvesOneIndexTest()
    {
        var errors = new List<string>();
        var indices = ParaViewFrameSelectionResolver.Resolve(new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Single, First = 4 }, 10, errors);

        Assert.That(errors, Is.Empty);
        Assert.That(indices, Is.EqualTo(new[] { 4 }));
    }

    [Test]
    public void RangeHonorsStepAndInclusiveLastTest()
    {
        var errors = new List<string>();
        var indices = ParaViewFrameSelectionResolver.Resolve(new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Range, First = 1, Last = 9, Step = 4 }, 10, errors);

        Assert.That(errors, Is.Empty);
        Assert.That(indices, Is.EqualTo(new[] { 1, 5, 9 }));
    }

    [Test]
    public void AllResolvesTheWholeTimelineOrOneForStaticTest()
    {
        var errors = new List<string>();
        Assert.That(ParaViewFrameSelectionResolver.Resolve(new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.All }, 3, errors), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(ParaViewFrameSelectionResolver.Resolve(new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.All }, 0, errors), Is.EqualTo(new[] { 0 }));
        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void ExplicitKeepsOrderTest()
    {
        var errors = new List<string>();
        var indices = ParaViewFrameSelectionResolver.Resolve(new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Explicit, Indices = [5, 0, 3] }, 6, errors);

        Assert.That(errors, Is.Empty);
        Assert.That(indices, Is.EqualTo(new[] { 5, 0, 3 }));
    }

    [Test]
    public void RangeOverflowsAreRejectedNotLoopedTest()
    {
        var errors = new List<string>();
        var wrapNegative = ParaViewFrameSelectionResolver.Resolve(new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Range, First = -10, Last = int.MaxValue - 5, Step = 1 }, 10, errors);
        var wrapStep = ParaViewFrameSelectionResolver.Resolve(new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Range, First = int.MaxValue - 100, Last = int.MaxValue, Step = 1 }, 10, errors);

        Assert.That(wrapNegative, Is.Empty);
        Assert.That(wrapStep, Is.Empty);
        Assert.That(errors, Has.Count.EqualTo(2));
    }

    [Test]
    public void InvalidSelectionsRecordErrorsTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Errors(new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Range, First = 5, Last = 2 }, 10), Has.Some.Contains("empty"));
            Assert.That(Errors(new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Range, First = 0, Last = 2, Step = 0 }, 10), Has.Some.Contains("step"));
            Assert.That(Errors(new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Explicit }, 10), Has.Some.Contains("lists no"));
            Assert.That(Errors(new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Explicit, Indices = [1, 1] }, 10), Has.Some.Contains("repeats"));
            Assert.That(Errors(new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Single, First = 10 }, 10), Has.Some.Contains("outside the timeline"));
            Assert.That(Errors(new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Single, First = -1 }, 10), Has.Some.Contains("outside the timeline"));
            Assert.That(Errors(new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Range, First = 0, Last = ParaViewInputLimits.MAX_OUTPUTS + 5 }, ParaViewInputLimits.MAX_OUTPUTS + 10), Has.Some.Contains("more than"));
            Assert.That(Errors(new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.All }, ParaViewInputLimits.MAX_OUTPUTS + 1), Has.Some.Contains("limit"));
        });

        static List<string> Errors(ParaViewFrameSelectionData selection, int count)
        {
            var errors = new List<string>();
            ParaViewFrameSelectionResolver.Resolve(selection, count, errors);
            return errors;
        }
    }
}
