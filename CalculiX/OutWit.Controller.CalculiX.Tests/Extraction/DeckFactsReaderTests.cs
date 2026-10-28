using OutWit.Controller.CalculiX.Extraction;

namespace OutWit.Controller.CalculiX.Tests.Extraction;

/// <summary>
/// The facts the extractor takes from a deck: steps and their procedures,
/// the OUTPUT= setting, element families, *TRANSFORM frames, node numbering
/// and the coordinates read on demand.
/// </summary>
[TestFixture]
public class DeckFactsReaderTests
{
    #region Tools

    private static string Fixture(string name)
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    }

    #endregion

    #region Read Tests

    [Test]
    public void StepsCarryTheirProcedureTest()
    {
        var facts = DeckFactsReader.Read(
        [
            "*STEP", "*STATIC", "1., 1.", "*END STEP",
            "*STEP, PERTURBATION", "*FREQUENCY", "5", "*END STEP",
            "*STEP", "*HEAT TRANSFER, STEADY STATE", "*END STEP",
            "*STEP", "*BUCKLE", "3", "*END STEP"
        ]);

        Assert.That(facts.Steps.Select(step => step.Procedure), Is.EqualTo(new[] { "STATIC", "FREQUENCY", "HEATTRANSFER", "BUCKLE" }));
        Assert.That(facts.Steps.Select(step => step.Number), Is.EqualTo(new[] { 1, 2, 3, 4 }));
        Assert.That(facts.Steps.Select(step => step.IsEigenvalue), Is.EqualTo(new[] { false, true, false, true }));
    }

    [Test]
    public void TheLastOutputOfTheFirstStepsFileCardsWinsTest()
    {
        // ccx keeps one OUTPUT= for every file card, settable in the first
        // step only; a later step's OUTPUT= is a warning there.
        var twoD = DeckFactsReader.Read(["*STEP", "*STATIC", "*NODE FILE, OUTPUT=3D", "U", "*EL FILE, OUTPUT=2D", "S", "*END STEP"]);
        var threeD = DeckFactsReader.Read(["*STEP", "*STATIC", "*NODE FILE, OUTPUT=2D", "U", "*EL FILE, OUTPUT=3D", "S", "*END STEP"]);
        var later = DeckFactsReader.Read(["*STEP", "*STATIC", "*NODE FILE", "U", "*END STEP", "*STEP", "*STATIC", "*NODE FILE, OUTPUT=2D", "U", "*END STEP"]);

        Assert.That(twoD.OutputIs2D, Is.True);
        Assert.That(threeD.OutputIs2D, Is.False);
        Assert.That(later.OutputIs2D, Is.False, "OUTPUT=2D in the second step has no effect");
    }

    [Test]
    public void ElementFamiliesAreToldApartTest()
    {
        var facts = DeckFactsReader.Read(["*ELEMENT, TYPE=C3D8, ELSET=SOLID", "1, 1, 2, 3, 4, 5, 6, 7, 8", "*ELEMENT, TYPE=S8R, ELSET=SKIN", "2, 1, 2, 3, 4, 5, 6, 7, 8"]);

        Assert.That(facts.ElementTypes, Is.EquivalentTo(new[] { "C3D8", "S8R" }));
        Assert.That(facts.HasExpandedElements, Is.True);
        Assert.That(facts.HasShellOrBeamElements, Is.True);
        Assert.That(DeckFacts.IsExpandedType("CPS8"), Is.True);
        Assert.That(DeckFacts.IsShellOrBeamType("CPS8"), Is.False, "a plane element has no bending stress to lose");
        Assert.That(DeckFacts.IsExpandedType("C3D20R"), Is.False);
        Assert.That(DeckFacts.IsShellOrBeamType("B32R"), Is.True);
    }

    [Test]
    public void TransformsBindTheirSetsAndTheLastOneWinsTest()
    {
        var facts = DeckFactsReader.Read(
        [
            "*NODE, NSET=NALL", "1, 0., 0., 0.", "2, 1., 0., 0.", "3, 2., 0., 0.",
            "*NSET, NSET=A", "1, 2",
            "*NSET, NSET=B", "2, 3",
            "*TRANSFORM, NSET=A, TYPE=C", "0., 5., 5., 100., 5., 5.",
            "*TRANSFORM, NSET=B", "0., 1., 0., -1., 0., 0."
        ]);

        Assert.That(facts.Transforms, Has.Count.EqualTo(2));
        Assert.That(facts.Transforms[0].Cylindrical, Is.True);
        Assert.That(facts.Transforms[0].Points, Is.EqualTo(new[] { 0.0, 5.0, 5.0, 100.0, 5.0, 5.0 }));
        Assert.That(facts.TransformOf(1)!.SetName, Is.EqualTo("A"));
        Assert.That(facts.TransformOf(2)!.SetName, Is.EqualTo("B"), "ccx assigns a node the last *TRANSFORM naming it");
        Assert.That(facts.TransformOf(3)!.Cylindrical, Is.False);
        Assert.That(facts.TransformOf(4), Is.Null);
        Assert.That(facts.MaxNodeId, Is.EqualTo(3));
        Assert.That(facts.NodeCount, Is.EqualTo(3));
    }

    [Test]
    public void CoordinatesAreReadForTheNodesAskedForTest()
    {
        var coordinates = DeckFactsReader.ReadCoordinates(Fixture("transform-cyl.inp"), new HashSet<int> { 45, 99 });

        Assert.That(coordinates.Keys, Is.EquivalentTo(new[] { 45, 99 }));
        Assert.That(coordinates[45], Is.EqualTo(new[] { 0.0, 5.0, 5.0 }));
        Assert.That(coordinates[99], Is.EqualTo(new[] { 100.0, 10.0, 10.0 }));
    }

    [Test]
    public void TheAuditDecksReadAsExpectedTest()
    {
        var cylinder = DeckFactsReader.Read(Fixture("transform-cyl.inp"));
        var shell = DeckFactsReader.Read(Fixture("shell-s8r-2d.inp"));
        var staticFrequency = DeckFactsReader.Read(Fixture("static-freq.inp"));

        Assert.That(cylinder.Transforms.Single().SetName, Is.EqualTo("FIX"));
        Assert.That(cylinder.NodeSets["FIX"], Has.Count.EqualTo(9));
        Assert.That(shell.OutputIs2D, Is.True);
        Assert.That(shell.HasShellOrBeamElements, Is.True);
        Assert.That(staticFrequency.Steps.Select(step => step.Procedure), Is.EqualTo(new[] { "STATIC", "FREQUENCY" }));
    }

    #endregion
}
