using OutWit.Controller.CalculiX.Extraction;
using OutWit.Controller.CalculiX.Model;

namespace OutWit.Controller.CalculiX.Tests.Extraction;

/// <summary>
/// Golden gates: the fixtures are verbatim ccx 2.22 output of the committed
/// decks (same pinned solver the controller bundles); the expected numbers
/// were computed independently from those files and are asserted here so any
/// reader drift is a red test, not a silently different response table.
/// </summary>
[TestFixture]
public class CcxResponseExtractorTests
{
    #region Tools

    private static string Fixture(string name)
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    }

    private static CcxResponseRowData Row(CcxExtraction extraction)
    {
        Assert.That(extraction.Failed, Is.False, string.Join("; ", extraction.Notes));
        return extraction.Row!;
    }

    private static double Value(CcxResponseRowData row, string name)
    {
        var match = row.Values.SingleOrDefault(value => value.Name == name);
        Assert.That(match, Is.Not.Null, $"response '{name}' missing; present: {string.Join(", ", row.Values.Select(v => v.Name))}");
        return match!.Value;
    }

    private static IEnumerable<string> Names(CcxResponseRowData row)
    {
        return row.Values.Select(value => value.Name);
    }

    private static IEnumerable<CcxNoteData> NotesOn(CcxExtraction extraction, string column)
    {
        return extraction.Notes.Where(note => note.Column == column);
    }

    private static CcxExtractionRequestData Probes(params (CcxProbeAggregate Aggregate, string Quantity, string Set)[] probes)
    {
        return new CcxExtractionRequestData
        {
            Probes = probes.Select(probe => new CcxProbeData { Aggregate = probe.Aggregate, Quantity = probe.Quantity, SetName = probe.Set }).ToList()
        };
    }

    #endregion

    #region Static Tests

    [Test]
    public void StaticDeckYieldsDisplacementStressAndReactionResponsesTest()
    {
        var extraction = CcxResponseExtractor.Extract(Fixture("static.inp"), Fixture("static.frd"), Fixture("static.dat"), null);
        var row = Row(extraction);

        Assert.That(Value(row, "max_disp_magnitude"), Is.EqualTo(1.185993e-2).Within(1e-8));
        Assert.That(Value(row, "max_disp_x"), Is.EqualTo(3.596860e-3).Within(1e-9));
        Assert.That(Value(row, "max_disp_y"), Is.EqualTo(4.136030e-4).Within(1e-10));
        Assert.That(Value(row, "max_disp_z"), Is.EqualTo(1.130120e-2).Within(1e-8));

        Assert.That(Value(row, "max_von_mises"), Is.EqualTo(687.0693).Within(1e-3));
        Assert.That(Value(row, "min_von_mises"), Is.EqualTo(180.5004).Within(1e-3));

        // The tip load is 4 x -25 in z; equilibrium puts +100 back into the
        // fixed set, with x/y sums cancelling by symmetry.
        Assert.That(Value(row, "rf_sum_z@fix"), Is.EqualTo(100.0).Within(1e-4));
        Assert.That(Value(row, "rf_sum_x@fix"), Is.EqualTo(0.0).Within(1e-4));
        Assert.That(Value(row, "rf_sum_y@fix"), Is.EqualTo(0.0).Within(1e-4));

        Assert.That(extraction.Notes, Is.Empty, "a single-step solid deck has nothing to remark on");
    }

    [Test]
    public void StaticProbesEvaluateOverNamedSetsTest()
    {
        var request = Probes(
            (CcxProbeAggregate.Max, "disp_magnitude", "TIP"),
            (CcxProbeAggregate.Max, "von_mises", "FIX"),
            (CcxProbeAggregate.Max, "temp", "TIP"),
            (CcxProbeAggregate.Max, "von_mises", "NO_SUCH_SET"));

        var row = Row(CcxResponseExtractor.Extract(Fixture("static.inp"), Fixture("static.frd"), Fixture("static.dat"), request));

        // The global displacement maximum sits on the loaded tip, so the TIP
        // probe must reproduce it exactly.
        Assert.That(Value(row, "max_disp_magnitude@tip"), Is.EqualTo(1.185993e-2).Within(1e-8));
        Assert.That(Value(row, "max_von_mises@fix"), Is.GreaterThan(0));

        // No temperatures in a static solve, no such set in the deck — both
        // probes are skipped, never fabricated.
        Assert.That(Names(row), Has.None.Contains("temp@tip"));
        Assert.That(Names(row), Has.None.Contains("no_such_set"));
    }

    [Test]
    public void ReactionSumsIgnoreTheDisplacementAndStressPrintsAfterThemTest()
    {
        var row = Row(CcxResponseExtractor.Extract(Fixture("static-prints.inp"), null, Fixture("static-prints.dat"), null));

        Assert.That(Value(row, "rf_sum_z@fix"), Is.EqualTo(100.0).Within(1e-4));
        Assert.That(Value(row, "rf_sum_x@fix"), Is.EqualTo(0.0).Within(1e-4));
        Assert.That(Value(row, "rf_sum_y@fix"), Is.EqualTo(0.0).Within(1e-4));
    }

    [Test]
    public void ReactionSumsPrintedInALocalFrameAreRotatedToGlobalTest()
    {
        // transform-cyl.inp: the fixed face under a cylindrical *TRANSFORM, the
        // tip loaded with 9 x (0, 50, -100). Summed as printed, the table said
        // 1262 / -2524 / 0; the global reaction is 0 / -450 / 900.
        var extraction = CcxResponseExtractor.Extract(Fixture("transform-cyl.inp"), null, Fixture("transform-cyl.dat"), null);
        var row = Row(extraction);

        Assert.That(Value(row, "rf_sum_x@fix"), Is.EqualTo(0.0).Within(1e-3));
        Assert.That(Value(row, "rf_sum_y@fix"), Is.EqualTo(-450.0).Within(1e-3));
        Assert.That(Value(row, "rf_sum_z@fix"), Is.EqualTo(900.0).Within(1e-3));
        Assert.That(NotesOn(extraction, "rf_sum_z@fix").Single().Kind, Is.EqualTo(CcxNoteKind.Info));
        Assert.That(NotesOn(extraction, "rf_sum_z@fix").Single().Text, Does.Contain("rotated to the global frame"));
    }

    [Test]
    public void LocalRowsWithoutTheDeckAreRefusedWithANoteTest()
    {
        var extraction = CcxResponseExtractor.Extract(null, null, Fixture("transform-cyl.dat"), null);
        var row = Row(extraction);

        Assert.That(Names(row), Has.None.StartsWith("rf_sum"), "never the sum of local components");
        var note = NotesOn(extraction, "rf_sum_z@fix").Single();
        Assert.That(note.Kind, Is.EqualTo(CcxNoteKind.Warning));
        Assert.That(note.Text, Does.Contain("GLOBAL=YES or TOTALS=YES"));
    }

    [Test]
    public void TheGlobalTotalOfTotalsYesIsTakenAsPrintedTest()
    {
        var row = Row(CcxResponseExtractor.Extract(null, null, Fixture("static-totals.dat"), null));

        Assert.That(Value(row, "rf_sum_z@fix"), Is.EqualTo(100.0).Within(1e-9));
        Assert.That(Value(row, "rf_sum_x@fix"), Is.EqualTo(0.0).Within(1e-9));
    }

    #endregion

    #region Heat Tests

    [Test]
    public void HeatDeckYieldsTemperatureRangeAndSetProbesTest()
    {
        var request = Probes(
            (CcxProbeAggregate.Avg, "temp", "XMAX"),
            (CcxProbeAggregate.Min, "temp", "XMIN"));

        var row = Row(CcxResponseExtractor.Extract(Fixture("heat.inp"), Fixture("heat.frd"), null, request));

        Assert.That(Value(row, "max_temp"), Is.EqualTo(300.0).Within(1e-6));
        Assert.That(Value(row, "min_temp"), Is.EqualTo(100.0).Within(1e-6));

        // The whole XMAX face is prescribed 300 and XMIN 100 — the probes
        // must land exactly on the boundary values.
        Assert.That(Value(row, "avg_temp@xmax"), Is.EqualTo(300.0).Within(1e-6));
        Assert.That(Value(row, "min_temp@xmin"), Is.EqualTo(100.0).Within(1e-6));

        // A steady solve has one step: no over-time response appears.
        Assert.That(Names(row), Has.None.EqualTo("max_temp_any_step"));
    }

    #endregion

    #region Frequency Tests

    [Test]
    public void FrequencyDeckYieldsEigenfrequenciesInCyclesTest()
    {
        var extraction = CcxResponseExtractor.Extract(null, Fixture("freq.frd"), Fixture("freq.dat"), null);
        var row = Row(extraction);

        Assert.That(Value(row, "eigenfrequency_1"), Is.EqualTo(2.229443e5).Within(1e0));
        Assert.That(Value(row, "eigenfrequency_2"), Is.EqualTo(2.229443e5).Within(1e0));
        Assert.That(Value(row, "eigenfrequency_3"), Is.EqualTo(4.113257e5).Within(1e0));
        Assert.That(Value(row, "eigenfrequency_4"), Is.EqualTo(6.997396e5).Within(1e0));
        Assert.That(Names(row), Has.None.StartsWith("eigenfrequency_5"),
            "a 4-mode deck reports 4 eigenfrequencies — nothing from the participation-factor tables");

        // The single-step names stay; the mode-shape columns say what they are.
        Assert.That(Value(row, "max_disp_magnitude"), Is.GreaterThan(0));
        Assert.That(NotesOn(extraction, "max_disp_magnitude").Single().Text, Does.Contain("mode shape (mode 4"));
    }

    [Test]
    public void BucklingDeckYieldsOneFactorPerModeTest()
    {
        var extraction = CcxResponseExtractor.Extract(null, null, Fixture("buckle.dat"), null);
        var row = Row(extraction);

        Assert.That(Names(row), Is.EqualTo(new[] { "buckling_factor_1", "buckling_factor_2", "buckling_factor_3" }));
        Assert.That(Value(row, "buckling_factor_3"), Is.EqualTo(699.4240).Within(1e-3));
        Assert.That(extraction.Notes, Is.Empty);
    }

    [Test]
    public void ABucklingDeckWithReactionPrintsReportsTheBaseStateNotTheLastModeTest()
    {
        // buckle-prints.inp: the reaction printed before the factors is the
        // base state's (the applied load, 900 in x); the three blocks printed
        // per mode after them are mode shapes - the last of them was the
        // "reaction" before.
        var extraction = CcxResponseExtractor.Extract(Fixture("buckle-prints.inp"), null, Fixture("buckle-prints.dat"), null);
        var row = Row(extraction);

        Assert.That(Names(row), Is.EqualTo(new[] { "buckling_factor_1", "buckling_factor_2", "buckling_factor_3", "rf_sum_x@fix", "rf_sum_y@fix", "rf_sum_z@fix" }));
        Assert.That(Value(row, "rf_sum_x@fix"), Is.EqualTo(900.0).Within(1e-3));
        Assert.That(extraction.Notes.Single().Text, Does.Contain("mode shapes and are not summed"));
    }

    [Test]
    public void AStaticStepFollowedByAFrequencyStepReportsBothUnderStepNamesTest()
    {
        // static-freq.inp: before, the last .frd block (the highest mode
        // shape) and the last forces block (a mode print) won the plain names:
        // max_von_mises 606266 for a static 362, rf_sum_z 0 for a static 900.
        var extraction = CcxResponseExtractor.Extract(Fixture("static-freq.inp"), Fixture("static-freq.frd"), Fixture("static-freq.dat"), null);
        var row = Row(extraction);

        Assert.That(Value(row, "max_von_mises[step 1]"), Is.EqualTo(362.2752).Within(1e-3));
        Assert.That(Value(row, "max_disp_magnitude[step 1]"), Is.EqualTo(1.194839).Within(1e-5));
        Assert.That(Value(row, "rf_sum_z@fix[step 1]"), Is.EqualTo(900.0).Within(1e-6));
        Assert.That(Value(row, "eigenfrequency_1[step 2]"), Is.EqualTo(1000.459).Within(1e-3));
        Assert.That(Value(row, "eigenfrequency_5[step 2]"), Is.EqualTo(8027.393).Within(1e-3));
        Assert.That(Value(row, "max_von_mises[step 2]"), Is.EqualTo(606265.86).Within(1e-1));
        Assert.That(Names(row), Has.None.EqualTo("max_von_mises"), "a multi-step deck names the step in every column");
        Assert.That(Names(row), Has.None.StartsWith("rf_sum_z@fix[step 2]"), "the per-mode forces of the frequency step are not reactions");
        Assert.That(NotesOn(extraction, "max_von_mises[step 2]").Single().Text, Does.StartWith("a mode shape"));
    }

    [Test]
    public void FrequencyStepsNumberTheirModesFromOneTest()
    {
        // freq-freq.inp: 5 modes, a static perturbation, 3 modes — numbered
        // 6..8 before, so a variant where ARPACK returned one mode fewer
        // shifted every later column.
        var row = Row(CcxResponseExtractor.Extract(Fixture("freq-freq.inp"), null, Fixture("freq-freq.dat"), null));

        Assert.That(Value(row, "eigenfrequency_5[step 1]"), Is.EqualTo(8027.393).Within(1e-3));
        Assert.That(Value(row, "eigenfrequency_1[step 3]"), Is.EqualTo(1281.772).Within(1e-3));
        Assert.That(Value(row, "eigenfrequency_3[step 3]"), Is.EqualTo(6446.695).Within(1e-3));
        Assert.That(Names(row), Has.None.Contains("eigenfrequency_6"));
        Assert.That(Value(row, "rf_sum_x@fix[step 2]"), Is.EqualTo(-45000.0).Within(1e-2));
    }

    [Test]
    public void CyclicSymmetryModesAreNamedByNodalDiameterTest()
    {
        var row = Row(CcxResponseExtractor.Extract(Fixture("cyclic-freq.inp"), null, Fixture("cyclic-freq.dat"), null));

        Assert.That(Value(row, "eigenfrequency_nd2_1"), Is.EqualTo(1563.217).Within(1e-3));
        Assert.That(Value(row, "eigenfrequency_nd2_3"), Is.EqualTo(5265.133).Within(1e-3));
        Assert.That(Names(row).Count(name => name.StartsWith("eigenfrequency_nd")), Is.EqualTo(9));
        Assert.That(Names(row), Has.None.EqualTo("eigenfrequency_1"));
    }

    [Test]
    public void ComplexFrequencyModesReportTheirCyclesTest()
    {
        var row = Row(CcxResponseExtractor.Extract(Fixture("complex-freq.inp"), null, Fixture("complex-freq.dat"), null));

        Assert.That(Value(row, "eigenfrequency_1[step 2]"), Is.EqualTo(841.8615).Within(1e-3), "the third number of the row, not the fourth (the imaginary part)");
        Assert.That(Value(row, "eigenfrequency_1[step 1]"), Is.EqualTo(987.7677).Within(1e-3));
    }

    #endregion

    #region Shell And Beam Tests

    [Test]
    public void ProbesOnAShellModelFindTheExpandedNodesTest()
    {
        // shell-s8r.inp: deck nodes 1-289, .frd nodes 370-1236 (OUTPUT=3D).
        // Every probe over a deck set was blank before.
        var request = Probes(
            (CcxProbeAggregate.Max, "disp_magnitude", "TIP"),
            (CcxProbeAggregate.Avg, "disp_magnitude", "TIP"),
            (CcxProbeAggregate.Max, "von_mises", "TIP"),
            (CcxProbeAggregate.Min, "von_mises", "FIX"));
        var extraction = CcxResponseExtractor.Extract(Fixture("shell-s8r.inp"), Fixture("shell-s8r.frd"), null, request);
        var row = Row(extraction);

        Assert.That(Value(row, "max_von_mises"), Is.EqualTo(75.0007).Within(1e-3), "the analytic 75 at the clamped end");
        Assert.That(Value(row, "max_disp_magnitude@tip"), Is.EqualTo(1.190874).Within(1e-5));
        Assert.That(Value(row, "avg_disp_magnitude@tip"), Is.EqualTo(1.190782).Within(1e-5));
        Assert.That(Value(row, "max_von_mises@tip"), Is.EqualTo(0.80943).Within(1e-4), "the free end carries no bending");
        Assert.That(Value(row, "min_von_mises@fix"), Is.EqualTo(0.43299).Within(1e-4));

        // The 4 mid-side nodes of the 9-node tip have no mid-surface node.
        var note = NotesOn(extraction, "max_disp_magnitude@tip").Single();
        Assert.That(note.Kind, Is.EqualTo(CcxNoteKind.Info));
        Assert.That(note.Text, Does.StartWith("4 of 9 nodes of set TIP have no result node at their own position"));
    }

    [Test]
    public void ProbesOnABeamModelTakeTheSectionNodesTest()
    {
        // beam-b32.inp: a beam node expands to its section's nodes, none at
        // the axis.
        var request = Probes((CcxProbeAggregate.Max, "von_mises", "TIP"), (CcxProbeAggregate.Max, "disp_magnitude", "TIP"));
        var row = Row(CcxResponseExtractor.Extract(Fixture("beam-b32.inp"), Fixture("beam-b32.frd"), null, request));

        Assert.That(Value(row, "max_von_mises"), Is.EqualTo(59.7327).Within(1e-3));
        Assert.That(Value(row, "max_disp_magnitude@tip"), Is.EqualTo(0.190934).Within(1e-5));
        Assert.That(Value(row, "max_von_mises@tip"), Is.EqualTo(5.59661).Within(1e-4));
    }

    [Test]
    public void VonMisesIsRefusedOnShellsWithOutput2DTest()
    {
        // shell-s8r-2d.inp: OUTPUT=2D maps the stress onto the mid-surface,
        // where the bending stress is gone - max_von_mises read 0.81 against
        // the 75 of the expanded mesh. Displacements are the shell's own.
        var request = Probes((CcxProbeAggregate.Max, "von_mises", "TIP"), (CcxProbeAggregate.Max, "disp_magnitude", "TIP"));
        var extraction = CcxResponseExtractor.Extract(Fixture("shell-s8r-2d.inp"), Fixture("shell-s8r-2d.frd"), null, request);
        var row = Row(extraction);

        Assert.That(Names(row), Has.None.Contains("von_mises"));
        Assert.That(Value(row, "max_disp_magnitude"), Is.EqualTo(1.19079).Within(1e-5));
        Assert.That(Value(row, "max_disp_magnitude@tip"), Is.EqualTo(1.19079).Within(1e-5), "with OUTPUT=2D the deck nodes are the result nodes");
        foreach (var column in new[] { "max_von_mises", "min_von_mises", "max_von_mises@tip" })
        {
            var note = NotesOn(extraction, column).Single();
            Assert.That(note.Kind, Is.EqualTo(CcxNoteKind.Warning));
            Assert.That(note.Text, Does.Contain("OUTPUT=2D"));
        }
    }

    #endregion

    #region Robustness Tests

    [Test]
    public void AlienFrdContentYieldsAnEmptyRowNotAnExceptionTest()
    {
        // The fake-solver .frd is deck text; a truncated or foreign file must
        // degrade to an empty row — extraction never fails a finished solve.
        var alien = Path.Combine(TestContext.CurrentContext.TestDirectory, $"alien_{Guid.NewGuid():N}.frd");
        File.WriteAllText(alien, "*HEADING\nnot an frd at all\n-1 garbage\n");

        var extraction = CcxResponseExtractor.Extract(null, alien, null, null);

        Assert.That(Row(extraction).Values, Is.Empty);
        Assert.That(extraction.Notes, Is.Empty);
    }

    [Test]
    public void TruncatedBlockLosesItsResponsesButNotTheRowTest()
    {
        // A node row cut mid-write leaves fewer numbers than the block's
        // component list promises. The damaged DISP block must empty ITS
        // response family only; the intact temperature block still reports.
        var frd = Path.Combine(TestContext.CurrentContext.TestDirectory, $"truncated_{Guid.NewGuid():N}.frd");
        File.WriteAllLines(frd,
        [
            " -4  NDTEMP      1    1",
            " -5  T           1    1",
            " -1         1 3.00000E+02",
            " -1         2 1.00000E+02",
            " -3",
            " -4  DISP        4    1",
            " -5  D1          1    2    1    0",
            " -5  D2          1    2    2    0",
            " -5  D3          1    2    3    0",
            " -1         1 1.00000E-03",
            " -3"
        ]);

        var row = Row(CcxResponseExtractor.Extract(null, frd, null, null));

        Assert.That(Value(row, "max_temp"), Is.EqualTo(300.0).Within(1e-9));
        Assert.That(Value(row, "min_temp"), Is.EqualTo(100.0).Within(1e-9));
        Assert.That(Names(row), Has.None.StartsWith("max_disp"),
            "the truncated displacement block yields nothing — and kills nothing else");
    }

    [Test]
    public void AValueThatIsNotANumberEmptiesItsFamilyWithANoteTest()
    {
        // "nan" parsed to NaN (and poisoned the app's cache); "-nan(ind)"
        // parsed to 0. Both now empty the family, naming the node.
        var frd = Path.Combine(TestContext.CurrentContext.TestDirectory, $"nan_{Guid.NewGuid():N}.frd");
        File.WriteAllLines(frd,
        [
            " -4  NDTEMP      1    1",
            " -5  T           1    1",
            " -1         1 3.00000E+02",
            " -3",
            " -4  DISP        4    1",
            " -5  D1          1    2    1    0",
            " -5  D2          1    2    2    0",
            " -5  D3          1    2    3    0",
            " -1         1 1.00000E-03 2.00000E-03 3.00000E-03",
            " -1        57   -nan(ind) 2.00000E-03 3.00000E-03",
            " -3",
            " -4  STRESS      6    1",
            " -5  SXX         1    4    1    1",
            " -5  SYY         1    4    2    2",
            " -5  SZZ         1    4    3    3",
            " -5  SXY         1    4    1    2",
            " -5  SYZ         1    4    2    3",
            " -5  SZX         1    4    3    1",
            " -1         1 1.00000E+02 0.00000E+00 0.00000E+00 0.00000E+00 0.00000E+00 0.00000E+00",
            " -1         2         nan 0.00000E+00 0.00000E+00 0.00000E+00 0.00000E+00 0.00000E+00",
            " -3"
        ]);

        var extraction = CcxResponseExtractor.Extract(null, frd, null, null);
        var row = Row(extraction);

        Assert.That(Names(row), Is.EqualTo(new[] { "max_temp", "min_temp" }));
        Assert.That(row.Values.Select(value => value.Value), Has.None.NaN);
        Assert.That(NotesOn(extraction, "max_disp_magnitude").Single().Text, Does.Contain("DISP at node 57 reads '-nan(ind)'"));
        Assert.That(NotesOn(extraction, "max_von_mises").Single().Text, Does.Contain("STRESS at node 2 reads 'nan'"));
        Assert.That(extraction.Notes.Select(note => note.Kind), Is.All.EqualTo(CcxNoteKind.Warning));
    }

    [Test]
    public void AReaderThatThrowsFailsTheExtractionNeverAnEmptySuccessTest()
    {
        // A path that cannot be read as a file: the extraction has no row to
        // offer and says so, instead of an empty row passing for a solve with
        // nothing to report.
        var extraction = CcxResponseExtractor.Extract(null, TestContext.CurrentContext.TestDirectory, null, null);

        Assert.That(extraction.Failed, Is.True);
        Assert.That(extraction.Row, Is.Null);
        var error = extraction.Notes.Single();
        Assert.That(error.Kind, Is.EqualTo(CcxNoteKind.Error));
        Assert.That(error.Text, Does.StartWith("response extraction failed"));
    }

    [Test]
    public void AnUnreadableDeckLeavesANoteAndTheFileResponsesTest()
    {
        var extraction = CcxResponseExtractor.Extract(TestContext.CurrentContext.TestDirectory, Fixture("static.frd"), Fixture("static.dat"), null);
        var row = Row(extraction);

        Assert.That(Value(row, "max_von_mises"), Is.EqualTo(687.0693).Within(1e-3));
        Assert.That(extraction.Notes.Single().Text, Does.StartWith("the deck could not be read"));
    }

    #endregion
}
