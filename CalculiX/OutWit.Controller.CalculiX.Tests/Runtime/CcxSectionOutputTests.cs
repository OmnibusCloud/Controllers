using OutWit.Controller.CalculiX.Extraction;
using OutWit.Controller.CalculiX.Runtime;

namespace OutWit.Controller.CalculiX.Tests.Runtime;

/// <summary>
/// The output a deck that writes OUTPUT=2D gets before the solve for its
/// shell and beam stress - SECTION FORCES for the beams, one section print
/// per shell edge - and the edge prints leaving the .dat afterwards.
/// </summary>
[TestFixture]
public class CcxSectionOutputTests
{
    private string m_directory = null!;

    [SetUp]
    public void Setup()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"ccx-section-{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(m_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    #region Tools

    private static string Fixture(string name)
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    }

    private static List<string> Model(bool shells, bool beams)
    {
        var lines = new List<string> { "*NODE", "1, 0, 0, 0", "2, 1, 0, 0", "3, 1, 1, 0", "4, 0, 1, 0", "5, 0, 0, 5", "6, 1, 0, 5" };
        if (shells)
            lines.AddRange(["*ELEMENT, TYPE=S4, ELSET=PLATE", "1, 1, 2, 3, 4", "*SHELL SECTION, ELSET=PLATE, MATERIAL=STEEL", "0.1"]);

        if (beams)
            lines.AddRange(["*ELEMENT, TYPE=B31, ELSET=BEAM", "2, 5, 6", "*BEAM SECTION, ELSET=BEAM, MATERIAL=STEEL, SECTION=RECT", "1., 1.", "0., 1., 0."]);

        lines.AddRange(["*MATERIAL, NAME=STEEL", "*ELASTIC", "210000., 0.3"]);
        return lines;
    }

    private static IEnumerable<string> Step(string procedure, string output = ", OUTPUT=2D")
    {
        return ["*STEP", procedure, $"*NODE FILE{output}", "U", "*EL FILE", "S", "*END STEP"];
    }

    #endregion

    #region Rewrite Tests

    [Test]
    public void ABeamDeckGetsSectionForcesInItsFirstStepTest()
    {
        List<string> deck = [.. Model(shells: false, beams: true), .. Step("*STATIC"), .. Step("*STATIC", string.Empty)];

        var rewritten = CcxSectionOutput.Rewrite(deck, out var notes, out var shellPrints);

        var expected = new List<string>(deck);
        expected.Insert(expected.IndexOf("*END STEP"), "*EL FILE, OUTPUT=2D, SECTION FORCES");
        Assert.That(rewritten, Is.EqualTo(expected), "at the end of step 1, after the deck's own OUTPUT=2D; ccx keeps it for later steps");
        Assert.That(shellPrints, Is.False);
        Assert.That(notes.Single().Kind, Is.EqualTo(CcxNoteKind.Info));
        Assert.That(notes.Single().Text, Does.StartWith("*EL FILE, OUTPUT=2D, SECTION FORCES was added to step 1"));
    }

    [Test]
    public void AShellDeckGetsAPrintPerEdgeInItsStaticStepsOnlyTest()
    {
        List<string> deck = [.. Model(shells: true, beams: false), .. Step("*STATIC"), .. Step("*STATIC"), .. Step("*FREQUENCY"), .. Step("*STATIC")];

        var rewritten = CcxSectionOutput.Rewrite(deck, out var notes, out var shellPrints);

        // Four edges: the surfaces before the first step, the prints in
        // step 1 (step 2 inherits them), off in the frequency step 3, back in step 4.
        string[] prints =
        [
            "*SECTION PRINT, SURFACE=WITEDGE1, NAME=WITPRINT1", "SOF", "*SECTION PRINT, SURFACE=WITEDGE2, NAME=WITPRINT2", "SOF",
            "*SECTION PRINT, SURFACE=WITEDGE3, NAME=WITPRINT3", "SOF", "*SECTION PRINT, SURFACE=WITEDGE4, NAME=WITPRINT4", "SOF"
        ];
        List<string> expected =
        [
            .. Model(shells: true, beams: false),
            "*SURFACE, NAME=WITEDGE1, TYPE=ELEMENT", "1, S3", "*SURFACE, NAME=WITEDGE2, TYPE=ELEMENT", "1, S4",
            "*SURFACE, NAME=WITEDGE3, TYPE=ELEMENT", "1, S5", "*SURFACE, NAME=WITEDGE4, TYPE=ELEMENT", "1, S6",
            .. Step("*STATIC").SkipLast(1), .. prints, "*END STEP",
            .. Step("*STATIC"),
            .. Step("*FREQUENCY").SkipLast(1), "*SECTION PRINT, SURFACE=WITEDGE1, NAME=WITPRINTOFF3, FREQUENCYF=0", "SOF", "*END STEP",
            .. Step("*STATIC").SkipLast(1), .. prints, "*END STEP"
        ];

        Assert.That(rewritten, Is.EqualTo(expected));
        Assert.That(shellPrints, Is.True);
        Assert.That(notes.Single().Text, Does.StartWith("4 *SECTION PRINT requests, one per shell element edge, were added"));

        // The rewritten deck is recognized as such and left alone.
        Assert.That(CcxSectionOutput.Rewrite(rewritten, out var again, out _), Is.EqualTo(rewritten));
        Assert.That(again, Is.Empty);
    }

    [Test]
    public void DecksThatNeedNothingOrCannotBeServedAreLeftAsTheyAreTest()
    {
        List<string>[] decks =
        [
            // The expanded mesh carries the stress.
            [.. Model(shells: true, beams: true), .. Step("*STATIC", string.Empty)],
            // No stress requested.
            [.. Model(shells: true, beams: false), "*STEP", "*STATIC", "*NODE FILE, OUTPUT=2D", "U", "*END STEP"],
            // Solids only.
            ["*NODE", "1, 0, 0, 0", "*ELEMENT, TYPE=C3D8, ELSET=SOLID", "1, 1, 2, 3, 4, 5, 6, 7, 8", .. Step("*STATIC")],
            // An S3 among the shells: the beams alone would not cover the model either.
            [.. Model(shells: true, beams: true), "*ELEMENT, TYPE=S3, ELSET=PLATE", "7, 1, 2, 3", .. Step("*STATIC")]
        ];

        foreach (var deck in decks)
        {
            Assert.That(CcxSectionOutput.Rewrite(deck, out var notes, out var shellPrints), Is.EqualTo(deck));
            Assert.That(notes, Is.Empty);
            Assert.That(shellPrints, Is.False);
        }
    }

    [Test]
    public void ApplyRewritesTheDeckFileInPlaceTest()
    {
        var path = Path.Combine(m_directory, "job.inp");
        File.WriteAllLines(path, [.. Model(shells: true, beams: true), .. Step("*STATIC")]);

        var notes = CcxSectionOutput.Apply(path, out var shellPrints);

        Assert.That(notes, Has.Count.EqualTo(2));
        Assert.That(shellPrints, Is.True);
        var text = File.ReadAllText(path);
        Assert.That(text, Does.Contain("*EL FILE, OUTPUT=2D, SECTION FORCES").And.Contain("*SECTION PRINT, SURFACE=WITEDGE4, NAME=WITPRINT4"));
    }

    #endregion

    #region Strip Tests

    [Test]
    public void TheEdgePrintsLeaveTheDatAndTheDecksOwnPrintsStayTest()
    {
        // The S8R fixture prints its reactions (*NODE PRINT, NSET=FIX) beside
        // the controller's 52 edge prints.
        var path = Path.Combine(m_directory, "job.dat");
        File.Copy(Fixture("shell-s8r-2d-edges.dat"), path);
        var before = DatResultReader.Read(path);

        var removed = CcxSectionOutput.StripDat(path);

        Assert.That(removed, Is.EqualTo(52));
        var text = File.ReadAllText(path);
        Assert.That(text, Does.Not.Contain("statistics for surface set"));
        Assert.That(text, Does.Contain(" forces (fx,fy,fz) for set FIX and time  0.1000000E+01"));
        Assert.That(text.Replace("\r\n", string.Empty), Does.Not.Contain("\n"), "the CRLF lines of the Windows ccx stay CRLF");

        var after = DatResultReader.Read(path);
        Assert.That(after.Steps.Single().Sections, Is.Empty);
        Assert.That(after.Steps.Single().ForceBlocks.Single().Rows.Select(row => row.Fz),
            Is.EqualTo(before.Steps.Single().ForceBlocks.Single().Rows.Select(row => row.Fz)));
    }

    [Test]
    public void ADatOfEdgePrintsAloneComesBackEmptyTest()
    {
        // The converted plate prints nothing of its own: ccx leaves its .dat
        // empty without the edge prints, and so does the strip.
        var path = Path.Combine(m_directory, "job.dat");
        File.Copy(Fixture("shell-s4-2d-20x2.dat"), path);

        Assert.That(CcxSectionOutput.StripDat(path), Is.EqualTo(102));
        Assert.That(new FileInfo(path).Length, Is.Zero);
    }

    [Test]
    public void AStepThatPrintedOnlyEdgesLosesItsHeaderTest()
    {
        var path = Path.Combine(m_directory, "job.dat");
        File.WriteAllLines(path,
        [
            "", "                        S T E P       1", "", "", "                                INCREMENT     1", "", "",
            " forces (fx,fy,fz) for set FIX and time  0.1000000E+01", "", "         1  1.0E+00  2.0E+00  3.0E+00",
            "", "", "", " statistics for surface set WITEDGE1 and time  0.1000000E+01", "",
            "   total surface force (fx,fy,fz) and moment about the origin(mx,my,mz)", "", "    1.0E+00  2.0E+00  3.0E+00  4.0E+00  5.0E+00  6.0E+00",
            "", "   center of gravity and mean normal", "", "    1.0E+00  2.0E+00  3.0E+00  4.0E+00  5.0E+00  6.0E+00",
            "", "   moment about the center of gravity(mx,my,mz)", "", "    1.0E+00  2.0E+00  3.0E+00",
            "", "   area, normal force (+ = tension), shear force (size), torque and bending moment (size)", "", "    1.0E+00  2.0E+00  3.0E+00  4.0E+00  5.0E+00",
            "", "                        S T E P       2", "", "", "                                INCREMENT     1", "",
            "", "", " statistics for surface set WITEDGE1 and time  0.2000000E+01", "",
            "   total surface force (fx,fy,fz) and moment about the origin(mx,my,mz)", "", "    1.0E+00  2.0E+00  3.0E+00  4.0E+00  5.0E+00  6.0E+00",
            "", "   center of gravity and mean normal", "", "    1.0E+00  2.0E+00  3.0E+00  4.0E+00  5.0E+00  6.0E+00",
            "", "   moment about the center of gravity(mx,my,mz)", "", "    1.0E+00  2.0E+00  3.0E+00",
            "", "   area, normal force (+ = tension), shear force (size), torque and bending moment (size)", "", "    1.0E+00  2.0E+00  3.0E+00  4.0E+00  5.0E+00"
        ]);

        Assert.That(CcxSectionOutput.StripDat(path), Is.EqualTo(2));
        Assert.That(File.ReadAllLines(path), Is.EqualTo(new[]
        {
            "", "                        S T E P       1", "", "", "                                INCREMENT     1", "", "",
            " forces (fx,fy,fz) for set FIX and time  0.1000000E+01", "", "         1  1.0E+00  2.0E+00  3.0E+00"
        }));
    }

    #endregion
}
