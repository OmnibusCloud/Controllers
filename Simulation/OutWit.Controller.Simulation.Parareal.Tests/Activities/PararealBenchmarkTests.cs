using OutWit.Controller.Simulation.Parareal.Utils;

namespace OutWit.Controller.Simulation.Parareal.Tests.Activities;

/// <summary>
/// The benchmark path calibrates pool scheduling on real nodes — a throw or a
/// drifted unit string breaks allocation silently. Smoke-tested at a small
/// size; the full 40³ run is exercised on live nodes by BenchmarkRunner.
/// </summary>
[TestFixture]
public class PararealBenchmarkTests
{
    [Test]
    public void MeasureProducesCalibratedResultTest()
    {
        var result = PararealBenchmark.Measure(gridSize: 9, stepCount: 3);

        Assert.That(result.Rate, Is.GreaterThan(0).And.Not.EqualTo(double.PositiveInfinity));
        Assert.That(result.Unit, Is.EqualTo(PararealBenchmark.UNIT));
        Assert.That(result.Iterations, Is.EqualTo(3));
        Assert.That(result.Elapsed, Is.GreaterThan(TimeSpan.Zero));
        Assert.That(result.Custom!["gridSize"], Is.EqualTo("9"));
    }

    [Test]
    public void MeasureIsDeterministicTest()
    {
        var first = PararealBenchmark.Measure(gridSize: 9, stepCount: 3);
        var second = PararealBenchmark.Measure(gridSize: 9, stepCount: 3);

        Assert.That(second.Custom!["checksum"], Is.EqualTo(first.Custom!["checksum"]),
            "the benchmark computation must be bitwise-deterministic (B-4)");
    }

    [Test]
    public void UnitStringIsPinnedTest()
    {
        Assert.That(PararealBenchmark.UNIT, Is.EqualTo("slab-step@40^3-v1"));
        Assert.That(PararealBenchmark.REFERENCE_SIZE, Is.EqualTo(40));
        Assert.That(PararealBenchmark.STEP_COUNT, Is.EqualTo(20));
    }
}
