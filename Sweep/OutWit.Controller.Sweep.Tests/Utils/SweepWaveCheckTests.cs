using OutWit.Controller.Sweep.Utils;

namespace OutWit.Controller.Sweep.Tests.Utils;

[TestFixture]
public class SweepWaveCheckTests
{
    #region Check Tests

    [Test]
    public void AWaveThatIsTheChunkInAnyOrderHasNoFindingsTest()
    {
        Assert.That(SweepWaveCheck.Findings([4, 5, 6], [6, 4, 5]), Is.Empty);
    }

    [Test]
    public void AShortWaveNamesTheMissingVariantsTest()
    {
        Assert.That(SweepWaveCheck.Findings([4, 5, 6], [5]), Is.EqualTo(new[] { "missing variant(s) #4, #6" }));
    }

    [Test]
    public void AVariantTwiceIsNamedEvenWhenTheCountMatchesTest()
    {
        // The count alone passes this wave: two results for two tasks.
        Assert.That(SweepWaveCheck.Findings([4, 5], [4, 4]), Is.EqualTo(new[] { "missing variant(s) #5", "variant(s) #4 more than once" }));
    }

    [Test]
    public void AVariantFromOutsideTheChunkIsNamedTest()
    {
        Assert.That(SweepWaveCheck.Findings([4, 5], [4, 9]), Is.EqualTo(new[] { "missing variant(s) #5", "variant(s) #9 not in the chunk" }));
    }

    [Test]
    public void ALongWaveNamesWhatItCarriesBeyondTheChunkTest()
    {
        Assert.That(SweepWaveCheck.Findings([4, 5], [5, 4, 5, 7]), Is.EqualTo(new[] { "variant(s) #5 more than once", "variant(s) #7 not in the chunk" }));
    }

    #endregion
}
