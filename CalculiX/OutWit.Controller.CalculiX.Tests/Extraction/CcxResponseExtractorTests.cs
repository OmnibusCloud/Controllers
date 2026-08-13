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

    private static double Value(CcxResponseRowData row, string name)
    {
        var match = row.Values.SingleOrDefault(value => value.Name == name);
        Assert.That(match, Is.Not.Null, $"response '{name}' missing; present: {string.Join(", ", row.Values.Select(v => v.Name))}");
        return match!.Value;
    }

    #endregion

    #region Static Tests

    [Test]
    public void StaticDeckYieldsDisplacementStressAndReactionResponsesTest()
    {
        var row = CcxResponseExtractor.Extract(Fixture("static.inp"), Fixture("static.frd"), Fixture("static.dat"), null);

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
    }

    [Test]
    public void StaticProbesEvaluateOverNamedSetsTest()
    {
        var request = new CcxExtractionRequestData
        {
            Probes =
            [
                new CcxProbeData { Aggregate = CcxProbeAggregate.Max, Quantity = "disp_magnitude", SetName = "TIP" },
                new CcxProbeData { Aggregate = CcxProbeAggregate.Max, Quantity = "von_mises", SetName = "FIX" },
                new CcxProbeData { Aggregate = CcxProbeAggregate.Max, Quantity = "temp", SetName = "TIP" },
                new CcxProbeData { Aggregate = CcxProbeAggregate.Max, Quantity = "von_mises", SetName = "NO_SUCH_SET" }
            ]
        };

        var row = CcxResponseExtractor.Extract(Fixture("static.inp"), Fixture("static.frd"), Fixture("static.dat"), request);

        // The global displacement maximum sits on the loaded tip, so the TIP
        // probe must reproduce it exactly.
        Assert.That(Value(row, "max_disp_magnitude@tip"), Is.EqualTo(1.185993e-2).Within(1e-8));
        Assert.That(Value(row, "max_von_mises@fix"), Is.GreaterThan(0));

        // No temperatures in a static solve, no such set in the deck — both
        // probes are skipped, never fabricated.
        Assert.That(row.Values.Select(value => value.Name), Has.None.Contains("temp@tip"));
        Assert.That(row.Values.Select(value => value.Name), Has.None.Contains("no_such_set"));
    }

    #endregion

    #region Heat Tests

    [Test]
    public void HeatDeckYieldsTemperatureRangeAndSetProbesTest()
    {
        var request = new CcxExtractionRequestData
        {
            Probes =
            [
                new CcxProbeData { Aggregate = CcxProbeAggregate.Avg, Quantity = "temp", SetName = "XMAX" },
                new CcxProbeData { Aggregate = CcxProbeAggregate.Min, Quantity = "temp", SetName = "XMIN" }
            ]
        };

        var row = CcxResponseExtractor.Extract(Fixture("heat.inp"), Fixture("heat.frd"), null, request);

        Assert.That(Value(row, "max_temp"), Is.EqualTo(300.0).Within(1e-6));
        Assert.That(Value(row, "min_temp"), Is.EqualTo(100.0).Within(1e-6));

        // The whole XMAX face is prescribed 300 and XMIN 100 — the probes
        // must land exactly on the boundary values.
        Assert.That(Value(row, "avg_temp@xmax"), Is.EqualTo(300.0).Within(1e-6));
        Assert.That(Value(row, "min_temp@xmin"), Is.EqualTo(100.0).Within(1e-6));

        // A steady solve has one step: no over-time response appears.
        Assert.That(row.Values.Select(value => value.Name), Has.None.EqualTo("max_temp_any_step"));
    }

    #endregion

    #region Frequency Tests

    [Test]
    public void FrequencyDeckYieldsEigenfrequenciesInCyclesTest()
    {
        var row = CcxResponseExtractor.Extract(null, Fixture("freq.frd"), Fixture("freq.dat"), null);

        Assert.That(Value(row, "eigenfrequency_1"), Is.EqualTo(2.229443e5).Within(1e0));
        Assert.That(Value(row, "eigenfrequency_2"), Is.EqualTo(2.229443e5).Within(1e0));
        Assert.That(Value(row, "eigenfrequency_3"), Is.EqualTo(4.113257e5).Within(1e0));
        Assert.That(Value(row, "eigenfrequency_4"), Is.EqualTo(6.997396e5).Within(1e0));
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

        var row = CcxResponseExtractor.Extract(null, alien, null, null);

        Assert.That(row.Values, Is.Empty);
    }

    [Test]
    public void TruncatedBlockLosesItsResponsesButNotTheRowTest()
    {
        // A node row cut mid-write leaves fewer numbers than the block's
        // component list promises — the value math over that block blows up.
        // The damaged DISP block must empty ITS response family only; the
        // intact temperature block still reports.
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

        var row = CcxResponseExtractor.Extract(null, frd, null, null);

        Assert.That(Value(row, "max_temp"), Is.EqualTo(300.0).Within(1e-9));
        Assert.That(Value(row, "min_temp"), Is.EqualTo(100.0).Within(1e-9));
        Assert.That(row.Values.Select(value => value.Name), Has.None.StartsWith("max_disp"),
            "the truncated displacement block yields nothing — and kills nothing else");
    }

    #endregion
}
