using OutWit.Controller.CalculiX.Runtime;
using OutWit.Controller.CalculiX.Tests.Utils;
using OutWit.Engine.Data.Benchmark;

namespace OutWit.Controller.CalculiX.Tests.Runtime;

[TestFixture]
public class CcxBenchmarkTests
{
    #region Constants

    private const string COLD_MARKER_VARIABLE = "FAKE_CCX_COLD_MARKER";

    private const string COLD_DELAY_VARIABLE = "FAKE_CCX_COLD_DELAY_MS";

    #endregion

    #region Benchmark Tests

    [Test]
    public async Task MeasureRunsTheEmbeddedDeckAndScoresTest()
    {
        var fakeCcx = FindFakeCcx();

        var result = await CcxBenchmark.MeasureAsync(fakeCcx);

        // The fake solver honors the bare-jobname contract, so the embedded
        // deck extraction, the spawn and the scoring all run for real; only
        // the physics is fake (its .frd carries no DISP, so no checksum).
        Assert.That(result.Unit, Is.EqualTo(CcxBenchmark.UNIT));
        Assert.That(result.Rate, Is.GreaterThan(0));
        Assert.That(result.Iterations, Is.InRange(CcxBenchmark.MIN_RUNS, CcxBenchmark.MAX_RUNS));
        Assert.That(result.Elapsed, Is.GreaterThan(TimeSpan.Zero));
        Assert.That(result.Custom, Is.Not.Null);
        Assert.That(result.Custom!["nodes"], Is.EqualTo(CcxReferenceDeck.NODES.ToString()));
        Assert.That(result.Custom["elements"], Is.EqualTo(CcxReferenceDeck.ELEMENTS.ToString()));
        Assert.That(result.Custom["threads"], Is.EqualTo(CcxProcessRunner.DefaultThreads().ToString()));
        Assert.That(result.Custom["runs_s"].Split(';'), Has.Length.EqualTo(result.Iterations));
        Assert.That(result.Custom.ContainsKey("warmup_s"), Is.True);
    }

    [Test]
    public async Task TheColdFirstStartIsAWarmUpAndDoesNotLowerTheRateTest()
    {
        var fakeCcx = FindFakeCcx();
        var marker = Path.Combine(Path.GetTempPath(), $"fake-ccx-cold-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable(COLD_MARKER_VARIABLE, marker);
        Environment.SetEnvironmentVariable(COLD_DELAY_VARIABLE, "1500");

        try
        {
            var result = await CcxBenchmark.MeasureAsync(fakeCcx);

            // The first start sleeps 1.5 s, like a node opening a freshly
            // extracted kit; it lands in the warm-up, never in the timed runs.
            var warmup = double.Parse(result.Custom!["warmup_s"], System.Globalization.CultureInfo.InvariantCulture);
            var median = double.Parse(result.Custom["median_s"], System.Globalization.CultureInfo.InvariantCulture);
            Assert.That(File.Exists(marker), Is.True);
            Assert.That(warmup, Is.GreaterThanOrEqualTo(1.5));
            Assert.That(median, Is.LessThan(1.0));
            Assert.That(result.Rate, Is.GreaterThan(1.0));
        }
        finally
        {
            Environment.SetEnvironmentVariable(COLD_MARKER_VARIABLE, null);
            Environment.SetEnvironmentVariable(COLD_DELAY_VARIABLE, null);
            File.Delete(marker);
        }
    }

    [Test]
    public async Task ALongTargetRunsUpToTheMaximumAndMoreWarmUpsAreHonouredTest()
    {
        var fakeCcx = FindFakeCcx();
        var options = new WitBenchmarkOptions
        {
            MinDuration = TimeSpan.FromMinutes(10),
            WarmupIterations = 2
        };

        var result = await CcxBenchmark.MeasureAsync(fakeCcx, options);

        Assert.That(result.Iterations, Is.EqualTo(CcxBenchmark.MAX_RUNS));
        Assert.That(result.Custom!["runs_s"].Split(';'), Has.Length.EqualTo(CcxBenchmark.MAX_RUNS));
    }

    [Test]
    public void MeasureFailsLoudlyOnAMissingSolverTest()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"no-ccx-{Guid.NewGuid():N}.exe");

        Assert.That(async () => await CcxBenchmark.MeasureAsync(missing), Throws.Exception);
    }

    #endregion

    #region Scoring Tests

    [Test]
    public void TheRateComesFromTheMedianRunNotFromAStallTest()
    {
        var runs = new[] { TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(5.0), TimeSpan.FromSeconds(1.2) };

        var result = CcxBenchmark.ToResult(runs, TimeSpan.FromSeconds(2.5));

        Assert.That(result.Rate, Is.EqualTo(1.0 / 1.2).Within(1e-9));
        Assert.That(result.Iterations, Is.EqualTo(3));
        Assert.That(result.Elapsed, Is.EqualTo(TimeSpan.FromSeconds(7.2)));
        Assert.That(result.Unit, Is.EqualTo(CcxBenchmark.UNIT));
        Assert.That(result.Custom!["median_s"], Is.EqualTo("1.200"));
        Assert.That(result.Custom["runs_s"], Is.EqualTo("1.000;5.000;1.200"));
        Assert.That(result.Custom["warmup_s"], Is.EqualTo("2.500"));
    }

    [Test]
    public void TheMedianOfAnEvenCountIsTheMeanOfTheMiddleTwoTest()
    {
        var runs = new[] { TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2) };

        Assert.That(CcxBenchmark.Median(runs), Is.EqualTo(TimeSpan.FromSeconds(2.5)));
    }

    [Test]
    public void ABusyMachineIsRatedAsBusyTest()
    {
        // Two slow runs out of three: the median is slow - the best run would hide it.
        var runs = new[] { TimeSpan.FromSeconds(3.0), TimeSpan.FromSeconds(0.9), TimeSpan.FromSeconds(2.8) };

        var result = CcxBenchmark.ToResult(runs, TimeSpan.FromSeconds(4));

        Assert.That(result.Rate, Is.EqualTo(1.0 / 2.8).Within(1e-9));
    }

    [Test]
    public void ScoringKeepsTheCallersMetadataTest()
    {
        var custom = new Dictionary<string, string> { ["nodes"] = "8000", ["checksum"] = "1.0E-01" };

        var result = CcxBenchmark.ToResult(new[] { TimeSpan.FromSeconds(0.5) }, TimeSpan.Zero, custom);

        Assert.That(result.Custom!["nodes"], Is.EqualTo("8000"));
        Assert.That(result.Custom["checksum"], Is.EqualTo("1.0E-01"));
        Assert.That(result.Iterations, Is.EqualTo(1));
        Assert.That(custom.ContainsKey("median_s"), Is.False);
    }

    [Test]
    public void ScoringWithoutARunFailsTest()
    {
        Assert.That(() => CcxBenchmark.ToResult(Array.Empty<TimeSpan>(), TimeSpan.Zero), Throws.ArgumentException);
        Assert.That(() => CcxBenchmark.ToResult(new[] { TimeSpan.Zero }, TimeSpan.Zero), Throws.ArgumentException);
    }

    #endregion

    #region Tools

    private static string FindFakeCcx()
    {
        var solutionRoot = CalculiXTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var fakeCcx = CalculiXTestPaths.FindFakeCcxPath(solutionRoot!);
        if (fakeCcx == null)
            Assert.Ignore("fake-ccx not built");

        return fakeCcx!;
    }

    #endregion
}
