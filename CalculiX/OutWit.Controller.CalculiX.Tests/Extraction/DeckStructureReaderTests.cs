using OutWit.Controller.CalculiX.Extraction;

namespace OutWit.Controller.CalculiX.Tests.Extraction;

/// <summary>
/// The shells and beams of a deck as the section-stress recovery reads them:
/// elements whose nodes run on over a second data line, sections assigned
/// through element sets of any construction, and the cards that rule a
/// recovery out or tell what the controller added.
/// </summary>
[TestFixture]
public class DeckStructureReaderTests
{
    #region Element Tests

    [Test]
    public void ElementsSectionsAndSetsAreReadAsCcxReadsThemTest()
    {
        var structure = DeckStructureReader.Read(
        [
            "*NODE", "1, 0, 0, 0", "2, 1, 0, 0", "3, 2, 0, 0",
            "*Element, Type = S8R, Elset = SKIN",
            "1, 1, 2, 3, 4, 5, 6,",
            "7, 8",
            "*ELEMENT, TYPE=S4",
            "2, 10, 11, 12, 13",
            "*ELEMENT, TYPE=B31, ELSET=SPAR",
            "3, 1, 2",
            "4, 2, 3",
            "*ELEMENT, TYPE=C3D8, ELSET=SOLID",
            "5, 1, 2, 3, 4, 5, 6, 7, 8",
            "*ELEMENT, TYPE=M3D4",
            "6, 20, 21, 22, 23",
            "*ELSET, ELSET=LOOSE, GENERATE",
            "2, 2, 1",
            "*ELSET, ELSET=ALLSHELLS",
            "SKIN, LOOSE",
            "*SHELL SECTION, ELSET=ALLSHELLS, MATERIAL=STEEL",
            "2.5",
            "*BEAM SECTION, ELSET=SPAR, MATERIAL=STEEL, SECTION=RECT",
            "10., 20.",
            "0., 0., 1."
        ]);

        Assert.That(structure.Shells.Keys, Is.EquivalentTo(new[] { 1, 2 }));
        Assert.That(structure.Shells[1].Nodes, Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }), "the nodes run on over the second line");
        Assert.That(structure.Beams.Keys, Is.EquivalentTo(new[] { 3, 4 }));
        Assert.That(structure.ShellThickness, Is.EquivalentTo(new Dictionary<int, double> { [1] = 2.5, [2] = 2.5 }));
        Assert.That(structure.BeamSections[3].Kind, Is.EqualTo("RECT"));
        Assert.That(structure.BeamSections[4].Dimensions, Is.EqualTo(new[] { 10.0, 20.0 }));
        Assert.That(structure.OtherMappedNodes, Is.EquivalentTo(new[] { 20, 21, 22, 23 }), "the membrane's nodes; the solid's are not mapped");
        Assert.That(structure.BeamNodeCoordinates[3].X, Is.EqualTo(2.0));
    }

    [Test]
    public void TheCardsThatRuleARecoveryOutAreSeenTest()
    {
        var structure = DeckStructureReader.Read(
        [
            "*MATERIAL, NAME=SOFT", "*DEFORMATION PLASTICITY", "210000., 0.3, 300., 9., 0.5",
            "*ELEMENT, TYPE=T3D2, ELSET=ROD", "1, 1, 2",
            "*STEP", "*STATIC",
            "*EL FILE, GLOBAL=NO", "E",
            "*SECTION PRINT, SURFACE=CUT, NAME=MINE", "SOF",
            "*END STEP"
        ]);

        Assert.That(structure.NonlinearMaterial, Is.EqualTo("*DEFORMATIONPLASTICITY"));
        Assert.That(structure.HasTrusses, Is.True);
        Assert.That(structure.LocalElementOutput, Is.True);
        Assert.That(structure.OwnSectionPrints, Is.True);
        Assert.That(structure.StressRequested, Is.False, "E is the strain");
    }

    [Test]
    public void TheControllersOwnAdditionsAreRecognizedTest()
    {
        var structure = DeckStructureReader.Read(
        [
            "*ELEMENT, TYPE=S4, ELSET=PLATE", "1, 1, 2, 3, 4",
            "*SURFACE, NAME=WITEDGE1, TYPE=ELEMENT", "1, S3",
            "*SURFACE, NAME=WITEDGE2, TYPE=ELEMENT", "1, S6",
            "*SURFACE, NAME=CUT, TYPE=ELEMENT", "1, S4",
            "*STEP", "*STATIC",
            "*EL FILE", "S",
            "*EL FILE, OUTPUT=2D, SECTION FORCES",
            "*SECTION PRINT, SURFACE=WITEDGE1, NAME=WITPRINT1", "SOF",
            "*END STEP"
        ]);

        Assert.That(structure.EdgeSurfaces, Is.EquivalentTo(new Dictionary<string, (int, int)> { ["WITEDGE1"] = (1, 3), ["WITEDGE2"] = (1, 6) }));
        Assert.That(structure.SectionForces, Is.True);
        Assert.That(structure.StressRequested, Is.True);
        Assert.That(structure.OwnSectionPrints, Is.False, "the controller's own print is not the deck's");
    }

    #endregion
}
