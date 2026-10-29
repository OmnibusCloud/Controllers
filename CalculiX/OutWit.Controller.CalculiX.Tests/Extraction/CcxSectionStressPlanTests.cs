using OutWit.Controller.CalculiX.Extraction;

namespace OutWit.Controller.CalculiX.Tests.Extraction;

/// <summary>
/// Which OUTPUT=2D decks get their shell and beam stress recovered, and why
/// the others do not: the plan is made from the deck's cards alone, before
/// the solve and again in the extraction.
/// </summary>
[TestFixture]
public class CcxSectionStressPlanTests
{
    #region Tools

    private static CcxSectionStressPlan Plan(IEnumerable<string> lines)
    {
        var deck = lines.ToList();
        return CcxSectionStressPlan.For(DeckFactsReader.Read(deck), DeckStructureReader.Read(deck));
    }

    // A 2 x 1 plate of quadrilaterals: 7 edges.
    private static IEnumerable<string> Plate(string type = "S4", string section = "*SHELL SECTION, ELSET=PLATE, MATERIAL=STEEL", string output = ", OUTPUT=2D")
    {
        return
        [
            "*NODE", "1, 0, 0, 0", "2, 1, 0, 0", "3, 2, 0, 0", "4, 0, 1, 0", "5, 1, 1, 0", "6, 2, 1, 0",
            $"*ELEMENT, TYPE={type}, ELSET=PLATE", "1, 1, 2, 5, 4", "2, 2, 3, 6, 5",
            "*MATERIAL, NAME=STEEL", "*ELASTIC", "210000., 0.3",
            section, "0.1",
            .. Step("*STATIC", output)
        ];
    }

    private static IEnumerable<string> Step(string procedure, string output = ", OUTPUT=2D")
    {
        return ["*STEP", procedure, $"*NODE FILE{output}", "U", "*EL FILE", "S", "*END STEP"];
    }

    // Beam elements along x unless the nodes say otherwise.
    private static IEnumerable<string> Beams(string type, string section, string dimensions, params string[] elements)
    {
        return
        [
            "*NODE", "11, 0, 0, 0", "12, 1, 0, 0", "13, 2, 0, 0", "14, 2, 1, 0", "15, 3, 0, 0",
            $"*ELEMENT, TYPE={type}, ELSET=BEAM", .. elements,
            "*MATERIAL, NAME=STEEL", "*ELASTIC", "210000., 0.3",
            section.Contains("PIPE") || section.Contains("BOX")
                ? $"*BEAM GENERAL SECTION, ELSET=BEAM, MATERIAL=STEEL, SECTION={section}"
                : $"*BEAM SECTION, ELSET=BEAM, MATERIAL=STEEL, SECTION={section}",
            dimensions, "0., 0., 1.",
            .. Step("*STATIC")
        ];
    }

    #endregion

    #region Shell Tests

    [Test]
    public void AQuadrilateralPlateIsRecoveredWithOnePrintPerEdgeTest()
    {
        var plan = Plan(Plate());

        Assert.That(plan.RecoversShells, Is.True, plan.ShellProblem);
        Assert.That(plan.RecoversAll, Is.True);

        // Seven edges, the shared one (2-5) printed once, on element 1.
        Assert.That(plan.ShellEdges, Is.EqualTo(new[] { (1, 3), (1, 4), (1, 5), (1, 6), (2, 3), (2, 4), (2, 5) }));
    }

    [Test]
    public void TheExpandedMeshNeedsNoRecoveryTest()
    {
        var plan = Plan(Plate(output: string.Empty));

        Assert.That(plan.Applies, Is.False);
        Assert.That(plan.RecoversAll, Is.False);
        Assert.That(plan.ShellProblem, Is.Null, "nothing to refuse: the expanded mesh has the stress");
    }

    [TestCase("S3")]
    [TestCase("S4R")]
    public void ShellsWhoseEdgesCcxIntegratesAtOnePointAreRefusedTest(string type)
    {
        string[] triangle = ["*NODE", "1, 0, 0, 0", "2, 1, 0, 0", "3, 0, 1, 0", "4, 1, 1, 0", $"*ELEMENT, TYPE={type}, ELSET=PLATE",
            type == "S3" ? "1, 1, 2, 3" : "1, 1, 2, 4, 3", "*SHELL SECTION, ELSET=PLATE, MATERIAL=STEEL", "0.1", .. Step("*STATIC")];

        var plan = Plan(triangle);

        Assert.That(plan.RecoversShells, Is.False);
        Assert.That(plan.ShellProblem, Is.EqualTo($"the deck has {type} elements, whose edge faces ccx integrates at one point, so their section prints carry no moment"));
    }

    [Test]
    public void ShellDecksTheRecoveryCannotReadAreRefusedTest()
    {
        Assert.That(Plan(Plate(section: "*SHELL SECTION, ELSET=PLATE, COMPOSITE")).ShellProblem, Does.Contain("COMPOSITE"));
        Assert.That(Plan(Plate(section: "*SHELL SECTION, ELSET=PLATE, MATERIAL=STEEL, NODAL THICKNESS")).ShellProblem, Does.Contain("NODAL THICKNESS"));
        Assert.That(Plan(Plate(section: "*SHELL SECTION, ELSET=OTHER, MATERIAL=STEEL")).ShellProblem, Does.Contain("without the thickness"));
        Assert.That(Plan(Plate().Prepend("*PLASTIC").Prepend("*MATERIAL, NAME=SOFT")).ShellProblem, Does.Contain("not linear elastic (*PLASTIC)"));
        Assert.That(Plan(Plate().Select(line => line == "*EL FILE" ? "*EL FILE, GLOBAL=NO" : line)).ShellProblem, Does.Contain("GLOBAL=NO"));
    }

    [Test]
    public void ADeckWithItsOwnSectionPrintsIsLeftAsItIsTest()
    {
        var deck = Plate().ToList();
        deck.InsertRange(deck.IndexOf("*END STEP"), ["*SECTION PRINT, SURFACE=CUT, NAME=MINE", "SOF"]);

        Assert.That(Plan(deck).ShellProblem, Is.EqualTo("the deck has *SECTION PRINT cards of its own, which the controller's prints would replace"));
    }

    [Test]
    public void ADeckWithoutAStaticStepIsRefusedTest()
    {
        var deck = Plate().TakeWhile(line => line != "*STEP").Concat(Step("*FREQUENCY"));

        Assert.That(Plan(deck).ShellProblem, Is.EqualTo("the recovery runs on *STATIC steps only"));
    }

    [Test]
    public void MoreEdgesThanTheCcxReaderCopesWithAreRefusedTest()
    {
        // 100 x 101 quadrilaterals: 100 * 102 + 101 * 101 = 20 401 edges.
        var lines = new List<string> { "*NODE" };
        for (var j = 0; j <= 101; j++)
        {
            for (var i = 0; i <= 100; i++)
                lines.Add($"{j * 101 + i + 1}, {i}, {j}, 0");
        }

        lines.Add("*ELEMENT, TYPE=S4, ELSET=PLATE");
        for (var j = 0; j < 101; j++)
        {
            for (var i = 0; i < 100; i++)
            {
                var n = j * 101 + i + 1;
                lines.Add($"{j * 100 + i + 1}, {n}, {n + 1}, {n + 102}, {n + 101}");
            }
        }

        lines.AddRange(["*SHELL SECTION, ELSET=PLATE, MATERIAL=STEEL", "0.1", .. Step("*STATIC")]);

        var plan = Plan(lines);

        Assert.That(plan.RecoversShells, Is.False);
        Assert.That(plan.ShellProblem, Does.StartWith($"the deck has 20401 shell edges, more than the {CcxSectionStressPlan.MAX_SHELL_EDGES}"));
    }

    #endregion

    #region Beam Tests

    [Test]
    public void AStraightBeamLineIsRecoveredTest()
    {
        var plan = Plan(Beams("B31", "RECT", "1., 2.", "1, 11, 12", "2, 12, 13", "3, 13, 15"));

        Assert.That(plan.RecoversBeams, Is.True, plan.BeamProblem);
        Assert.That(plan.RecoversAll, Is.True);
    }

    [Test]
    public void BeamDecksTheSectionForcesCannotServeAreRefusedTest()
    {
        Assert.That(Plan(Beams("B31R", "RECT", "1., 2.", "1, 11, 12")).BeamProblem,
            Is.EqualTo("the deck has B31R elements, whose section ccx integrates at one point, so their section forces carry no moment"));
        Assert.That(Plan(Beams("B32", "CIRC", "1., 1.", "1, 11, 12, 13")).BeamProblem, Does.Contain("CIRC section"));
        Assert.That(Plan(Beams("B32R", "BOX", "10., 20., 1., 2., 1., 1.", "1, 11, 12, 13")).BeamProblem, Does.Contain("unequal opposite walls"));
        Assert.That(Plan(Beams("B32R", "BOX", "10., 20., 1., 2., 1., 2.", "1, 11, 12, 13")).BeamProblem, Is.Null);
        Assert.That(Plan(Beams("B32R", "PIPE", "5., 1.", "1, 11, 12, 13")).BeamProblem, Is.Null);
        Assert.That(Plan(Beams("B33", "RECT", "1., 2.", "1, 11, 12, 13, 15")).BeamProblem, Is.EqualTo("the deck has B33 elements, which the recovery does not know"));
    }

    [Test]
    public void BeamsMeetingAtAnAngleOrAgainstEachOtherAreRefusedTest()
    {
        // 12-13 runs along x, 13-14 along y: a frame corner.
        Assert.That(Plan(Beams("B31", "RECT", "1., 2.", "1, 12, 13", "2, 13, 14")).BeamProblem,
            Is.EqualTo("1 beam nodes join beam elements at an angle, against each other's direction or of different sections, where ccx averages their section forces in different frames"));

        // In line, but the second element runs back towards the first.
        Assert.That(Plan(Beams("B31", "RECT", "1., 2.", "1, 11, 12", "2, 13, 12")).BeamProblem, Does.StartWith("1 beam nodes join"));

        // Three elements on one node.
        Assert.That(Plan(Beams("B31", "RECT", "1., 2.", "1, 11, 12", "2, 12, 13", "3, 12, 15")).BeamProblem, Does.StartWith("1 beam nodes join"));
    }

    [Test]
    public void BeamsOnShellOrTrussNodesAreRefusedTest()
    {
        var stiffened = Plate().ToList();
        stiffened.InsertRange(stiffened.IndexOf("*STEP"),
        [
            "*ELEMENT, TYPE=B31, ELSET=STIFFENER", "11, 1, 2", "12, 2, 3",
            "*BEAM SECTION, ELSET=STIFFENER, MATERIAL=STEEL, SECTION=RECT", "0.1, 0.5", "0., 0., 1."
        ]);

        var plan = Plan(stiffened);

        Assert.That(plan.RecoversShells, Is.True, "the plate alone could be recovered");
        Assert.That(plan.BeamProblem, Is.EqualTo("3 beam nodes are nodes of shell, membrane, plane or truss elements too, where ccx averages the beam's section forces with their stress"));
        Assert.That(plan.RecoversAll, Is.False, "the von Mises columns cover the whole model");

        var truss = Beams("B31", "RECT", "1., 2.", "1, 11, 12").ToList();
        truss.InsertRange(truss.IndexOf("*STEP"), ["*ELEMENT, TYPE=T3D2, ELSET=ROD", "5, 13, 15", "*SOLID SECTION, ELSET=ROD, MATERIAL=STEEL", "1."]);
        Assert.That(Plan(truss).BeamProblem, Does.Contain("truss elements"));
    }

    #endregion
}
