using System.Globalization;
using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Utils;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.OpenFOAM.Tests.Runtime;

[TestFixture]
public class FoamBenchmarkTests
{
    private FakeKit? m_kit;

    private string? m_temp;

    [TearDown]
    public void TearDown()
    {
        m_kit?.Dispose();
        if (m_temp != null)
            OpenFOAMTestPaths.TryDelete(m_temp);
    }

    #region Tools

    private FoamKit RequireKit()
    {
        var solutionRoot = OpenFOAMTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var fakeFoam = OpenFOAMTestPaths.FindFakeFoamPath(solutionRoot);
        if (fakeFoam == null)
            Assert.Ignore("fake-foam not built");

        m_kit = FakeKit.Create(fakeFoam, "blockMesh", "simpleFoam");
        return m_kit.Resolve();
    }

    private IWitTempStorage Temp()
    {
        m_temp ??= OpenFOAMTestPaths.CreateScratch("foam-temp");
        return new WitTempStorageDefault(m_temp);
    }

    #endregion

    #region Benchmark Tests

    [Test]
    public async Task MeasureMeshesOnceRunsTheReferenceCaseAndScoresTest()
    {
        var kit = RequireKit();

        var result = await FoamBenchmark.MeasureAsync(kit, Temp());

        // The fake solver honours the contract (cwd, log shape, a time
        // directory), so the copy, the controlDict rewrite, the spawns and
        // the scoring all run for real; only the physics is fake.
        Assert.That(result.Unit, Is.EqualTo(FoamBenchmark.UNIT));
        Assert.That(result.Rate, Is.GreaterThan(0));
        Assert.That(result.Iterations, Is.InRange(FoamBenchmark.MIN_RUNS, FoamBenchmark.MAX_RUNS));
        Assert.That(result.Elapsed, Is.GreaterThan(TimeSpan.Zero));
        Assert.That(result.Custom, Is.Not.Null);
        Assert.That(result.Custom!["iterations"], Is.EqualTo(FoamBenchmark.ITERATIONS.ToString(CultureInfo.InvariantCulture)));
        Assert.That(result.Custom["cells"], Is.EqualTo("12225"), "read from the fake solver's mesh report");
        Assert.That(result.Custom["platform"], Is.EqualTo("fake"));
        Assert.That(result.Custom["runs_s"].Split(';'), Has.Length.EqualTo(result.Iterations));
        Assert.That(result.Custom.ContainsKey("warmup_s"), Is.True);
        Assert.That(result.Custom.ContainsKey("checksum"), Is.True, "the last pressure residual marks the computation");
    }

    [Test]
    public async Task ALongTargetRunsUpToTheMaximumTest()
    {
        var kit = RequireKit();
        var options = new WitBenchmarkOptions { MinDuration = TimeSpan.FromMinutes(10), WarmupIterations = 2 };

        var result = await FoamBenchmark.MeasureAsync(kit, Temp(), options);

        Assert.That(result.Iterations, Is.EqualTo(FoamBenchmark.MAX_RUNS));
    }

    [Test]
    public void ATempFolderTooDeepForWindowsFailsTheBenchmarkWithTheReasonTest()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("the depth limit is a Windows one");

        var kit = RequireKit();
        Temp();
        var root = Path.Combine(new[] { m_temp ?? string.Empty }.Concat(Enumerable.Repeat("deepdeep", 16)).ToArray());

        // A failed benchmark is how the node leaves the Foam.Run pool: with
        // the reason, rather than taking variants it would fail one by one.
        var refusal = Assert.ThrowsAsync<InvalidOperationException>(() => FoamBenchmark.MeasureAsync(kit, new WitTempStorageDefault(root)));

        Assert.That(refusal!.Message, Does.Contain("too deep").And.Contain("Settings"));
    }

    #endregion

    #region Scoring Tests

    [Test]
    public void TheRateComesFromTheMedianRunNotFromAStallTest()
    {
        var runs = new[] { TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(5.0), TimeSpan.FromSeconds(1.2) };

        var result = FoamBenchmark.ToResult(runs, TimeSpan.FromSeconds(2.5));

        Assert.That(result.Rate, Is.EqualTo(1.0 / 1.2).Within(1e-9));
        Assert.That(result.Iterations, Is.EqualTo(3));
        Assert.That(result.Elapsed, Is.EqualTo(TimeSpan.FromSeconds(7.2)));
        Assert.That(result.Unit, Is.EqualTo(FoamBenchmark.UNIT));
        Assert.That(result.Custom!["median_s"], Is.EqualTo("1.200"));
        Assert.That(result.Custom["runs_s"], Is.EqualTo("1.000;5.000;1.200"));
        Assert.That(result.Custom["warmup_s"], Is.EqualTo("2.500"));
    }

    [Test]
    public void TheMedianOfAnEvenCountIsTheMeanOfTheMiddleTwoTest()
    {
        var runs = new[] { TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2) };

        Assert.That(FoamBenchmark.Median(runs), Is.EqualTo(TimeSpan.FromSeconds(2.5)));
    }

    [Test]
    public void ScoringWithoutARunFailsTest()
    {
        Assert.That(() => FoamBenchmark.ToResult(Array.Empty<TimeSpan>(), TimeSpan.Zero), Throws.ArgumentException);
        Assert.That(() => FoamBenchmark.ToResult(new[] { TimeSpan.Zero }, TimeSpan.Zero), Throws.ArgumentException);
    }

    #endregion
}
