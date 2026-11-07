using OutWit.Controller.CalculiX.Extraction;
using OutWit.Controller.CalculiX.Model;

namespace OutWit.Controller.CalculiX.Tests.Extraction;

/// <summary>
/// The von Mises stress of shells and beams under OUTPUT=2D, recovered from
/// section forces. The fixtures are verbatim ccx 2.22 output of the committed
/// decks as the controller rewrites them before the solve (edge section
/// prints for the shells, SECTION FORCES for the beams); the .dat still
/// carries the edge prints the adapter strips after the extraction. The
/// analytic root stress of each cantilever is in the deck's heading; ccx's
/// own answers are those of the expanded mesh.
/// </summary>
[TestFixture]
public class CcxSectionStressTests
{
    #region Tools

    private static string Fixture(string name)
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    }

    private static CcxExtraction Extract(string name, params (CcxProbeAggregate Aggregate, string Set)[] probes)
    {
        var dat = Fixture($"{name}.dat");
        var request = new CcxExtractionRequestData
        {
            Probes = probes.Select(probe => new CcxProbeData { Aggregate = probe.Aggregate, Quantity = "von_mises", SetName = probe.Set }).ToList()
        };

        return CcxResponseExtractor.Extract(Fixture($"{name}.inp"), Fixture($"{name}.frd"), File.Exists(dat) ? dat : null, request);
    }

    private static double Value(CcxExtraction extraction, string name)
    {
        Assert.That(extraction.Failed, Is.False, string.Join("; ", extraction.Notes));
        var match = extraction.Row!.Values.SingleOrDefault(value => value.Name == name);
        Assert.That(match, Is.Not.Null, $"response '{name}' missing; present: {string.Join(", ", extraction.Row.Values.Select(v => v.Name))}");
        return match!.Value;
    }

    private static void AssertRecovered(CcxExtraction extraction, string column, string method)
    {
        var notes = extraction.Notes.Where(note => note.Column == column).ToList();
        Assert.That(notes.Select(note => note.Kind), Is.EqualTo(new[] { CcxNoteKind.Info }), string.Join("; ", notes));
        Assert.That(notes[0].Text, Does.StartWith("von Mises from section forces (OUTPUT=2D gives the mid-surface or beam-axis stress)"));
        Assert.That(notes[0].Text, Does.Contain(method));
    }

    #endregion

    #region Shell Tests

    [Test]
    public void ShellPlateRecoversTheRootStressOfTheExpandedMeshTest()
    {
        // S8R cantilever plate 100 x 20 x 2, tip load 10: 6 P L / (b t^2) = 75
        // at the root. OUTPUT=2D alone read 0.81 (the mid-surface), the
        // expanded mesh 75.0.
        var extraction = Extract("shell-s8r-2d-edges", (CcxProbeAggregate.Max, "FIX"), (CcxProbeAggregate.Avg, "FIX"), (CcxProbeAggregate.Max, "TIP"));

        Assert.That(Value(extraction, "max_von_mises"), Is.EqualTo(75.0).Within(0.01));
        Assert.That(Value(extraction, "max_von_mises@fix"), Is.EqualTo(74.9995).Within(1e-3));
        Assert.That(Value(extraction, "avg_von_mises@fix"), Is.EqualTo(74.9995).Within(1e-3), "a beam-like plate: the same stress across the root");
        Assert.That(Value(extraction, "max_von_mises@tip"), Is.EqualTo(0.64949).Within(1e-4), "the free end carries the transverse shear only");

        foreach (var column in new[] { "max_von_mises", "min_von_mises", "max_von_mises@fix", "avg_von_mises@fix", "max_von_mises@tip" })
            AssertRecovered(extraction, column, "*SECTION PRINT of every element edge");

        Assert.That(extraction.Notes.Where(note => note.Kind != CcxNoteKind.Info), Is.Empty);
    }

    [Test]
    public void ConvertedNastranPlateApproachesTheAnalyticStressWithoutGrowingTest()
    {
        // A CQUAD4 strip 10 x 1 x 0.1 (nu 0, clamped, 1 at the tip) converted
        // from Nastran: 6 P L / (b t^2) = 6000 at the root. An S4 bends at
        // constant curvature, so the root reads the beam's stress half an
        // element in: 6000 (1 - h / 2L) - 5850 on 20 x 2 and 5925 on 40 x 4,
        // as the expanded mesh (OUTPUT=3D: 5850, 5925). Measured on the same
        // conversion (ccx 2.22, 2026-10-29): 5962.5 on 80 x 8, 5981.25 on
        // 160 x 16 - no singular growth at the clamp. OUTPUT=2D alone read 17.3.
        var coarse = Extract("shell-s4-2d-20x2");
        var fine = Extract("shell-s4-2d-40x4");

        Assert.That(Value(coarse, "max_von_mises"), Is.EqualTo(5850.0).Within(0.1));
        Assert.That(Value(fine, "max_von_mises"), Is.EqualTo(5925.0).Within(0.1));
        AssertRecovered(fine, "max_von_mises", "*SECTION PRINT of every element edge");
    }

    [Test]
    public void PressureOnAShellStripIsRecoveredTest()
    {
        // S8R strip 10 x 1 x 0.1 under a pressure of 0.001: q L^2 / 2 per
        // unit width, 3 q L^2 / t^2 = 30 at the root; 29.987 on 20 x 2
        // elements and 29.999 on 80 x 8 (measured), 29.950 on the 10 x 1 of
        // the fixture.
        var extraction = Extract("shell-s8r-2d-pressure");

        Assert.That(Value(extraction, "max_von_mises"), Is.EqualTo(29.950).Within(1e-3));
    }

    [Test]
    public void StaticStepsAreRecoveredAndAFrequencyStepIsRefusedTest()
    {
        // Static (tip load 10: 75), frequency (the edge prints switched off),
        // static (2.5 at the tip: 18.75).
        var extraction = Extract("shell-s8r-2d-steps");

        Assert.That(Value(extraction, "max_von_mises[step 1]"), Is.EqualTo(75.0).Within(0.01));
        Assert.That(Value(extraction, "max_von_mises[step 3]"), Is.EqualTo(18.75).Within(0.01));
        Assert.That(extraction.Row!.Values.Select(value => value.Name), Has.None.EqualTo("max_von_mises[step 2]"));

        var refusal = extraction.Notes.Single(note => note.Column == "max_von_mises[step 2]");
        Assert.That(refusal.Kind, Is.EqualTo(CcxNoteKind.Warning));
        Assert.That(refusal.Text, Does.Contain("the recovery runs on *STATIC steps only"));
    }

    [Test]
    public void AShellDeckWithoutEdgePrintsIsRefusedWithTheReasonTest()
    {
        // The deck as the user wrote it, solved without the controller's
        // additions: the mid-surface stress is all there is.
        var extraction = CcxResponseExtractor.Extract(Fixture("shell-s8r-2d.inp"), Fixture("shell-s8r-2d.frd"), null, null);

        Assert.That(extraction.Row!.Values.Select(value => value.Name), Has.None.Contains("von_mises"));
        var refusal = extraction.Notes.Single(note => note.Column == "max_von_mises");
        Assert.That(refusal.Kind, Is.EqualTo(CcxNoteKind.Warning));
        Assert.That(refusal.Text, Does.Contain("the deck carries no section prints of its shell edges"));
    }

    #endregion

    #region Beam Tests

    [Test]
    public void BeamB32RecoversTheRootStressTest()
    {
        // B32 cantilever 100 long, RECT 10 x 10, tip load 100: P L / (a b^2 / 6)
        // = 60 at the root; ccx's section moment there is 10 016.8, so 60.10.
        // OUTPUT=2D alone read 4.75, the expanded mesh 59.73.
        var extraction = Extract("beam-b32-2d-forces", (CcxProbeAggregate.Max, "FIX"), (CcxProbeAggregate.Max, "TIP"));

        Assert.That(Value(extraction, "max_von_mises"), Is.EqualTo(60.1008).Within(1e-3));
        Assert.That(Value(extraction, "max_von_mises@fix"), Is.EqualTo(60.1008).Within(1e-3));

        // At the free end only the shear is left: 1.5 V / A at the mid-sides.
        Assert.That(Value(extraction, "max_von_mises@tip"), Is.EqualTo(2.76734).Within(1e-4));
        AssertRecovered(extraction, "max_von_mises", "SECTION FORCES");
    }

    [Test]
    public void BeamB31RecoversTheStressOfItsElementTest()
    {
        // B31, the Nastran converter's beam: RECT 10 x 20, 100 across the 20 at
        // the tip of 100: 15 analytic at the root. A B31 bends at constant
        // curvature: the root moment is ccx's 9 728.3, the beam's half an
        // element in, so 14.59.
        var extraction = Extract("beam-b31-2d-forces");

        Assert.That(Value(extraction, "max_von_mises"), Is.EqualTo(14.5924).Within(1e-3));
    }

    [Test]
    public void ShellsAndBeamsOfOneDeckAreRecoveredTogetherTest()
    {
        // An S8R plate (75 at the root) and a separate B32 cantilever, RECT
        // 10 x 10 under 200 (120 at the root).
        var extraction = Extract("mixed-2d-sections", (CcxProbeAggregate.Max, "FIX"), (CcxProbeAggregate.Max, "BFIX"));

        Assert.That(Value(extraction, "max_von_mises"), Is.EqualTo(119.927).Within(1e-2));
        Assert.That(Value(extraction, "max_von_mises@fix"), Is.EqualTo(74.9995).Within(1e-3));
        Assert.That(Value(extraction, "max_von_mises@bfix"), Is.EqualTo(119.927).Within(1e-2));
        AssertRecovered(extraction, "max_von_mises", "*SECTION PRINT of every element edge");
        AssertRecovered(extraction, "max_von_mises", "SECTION FORCES");
    }

    #endregion
}
