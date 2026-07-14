using OutWit.Controller.Simulation.Schwarz.Utils;

namespace OutWit.Controller.Simulation.Schwarz.Tests.Activities;

/// <summary>
/// The benchmark path calibrates pool scheduling on real nodes — a throw or a
/// drifted unit string breaks allocation silently. Smoke-tested at a small
/// size; the full 40³ run is exercised on live nodes by BenchmarkRunner.
/// </summary>
[TestFixture]
public class SchwarzBenchmarkTests
{
    [Test]
    public void MeasureProducesCalibratedResultTest()
    {
        var result = SchwarzBenchmark.Measure(gridSize: 9, solveCount: 3);

        Assert.That(result.Rate, Is.GreaterThan(0).And.Not.EqualTo(double.PositiveInfinity));
        Assert.That(result.Unit, Is.EqualTo(SchwarzBenchmark.UNIT));
        Assert.That(result.Iterations, Is.EqualTo(3));
        Assert.That(result.Elapsed, Is.GreaterThan(TimeSpan.Zero));
        Assert.That(result.Custom, Is.Not.Null);
        Assert.That(result.Custom!["gridSize"], Is.EqualTo("9"));
    }

    [Test]
    public void MeasureIsDeterministicTest()
    {
        var first = SchwarzBenchmark.Measure(gridSize: 9, solveCount: 3);
        var second = SchwarzBenchmark.Measure(gridSize: 9, solveCount: 3);

        Assert.That(second.Custom!["checksum"], Is.EqualTo(first.Custom!["checksum"]),
            "the benchmark computation must be bitwise-deterministic (B-4)");
    }

    [Test]
    public void UnitStringIsPinnedTest()
    {
        // The unit string is the cross-fleet calibration contract — bump the
        // version suffix whenever the benchmark computation changes.
        Assert.That(SchwarzBenchmark.UNIT, Is.EqualTo("subdomain-solve@40^3-v1"));
        Assert.That(SchwarzBenchmark.REFERENCE_SIZE, Is.EqualTo(40));
        Assert.That(SchwarzBenchmark.SOLVE_COUNT, Is.EqualTo(20));
    }
}
