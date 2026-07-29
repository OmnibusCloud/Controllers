using OutWit.Controller.CalculiX.Runtime;
using OutWit.Controller.CalculiX.Tests.Utils;

namespace OutWit.Controller.CalculiX.Tests.Runtime;

[TestFixture]
public class CcxBenchmarkTests
{
    #region Benchmark Tests

    [Test]
    public async Task MeasureRunsTheEmbeddedDeckAndScoresTest()
    {
        var solutionRoot = CalculiXTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var fakeCcx = CalculiXTestPaths.FindFakeCcxPath(solutionRoot);
        if (fakeCcx == null)
            Assert.Ignore("fake-ccx not built");

        var result = await CcxBenchmark.MeasureAsync(fakeCcx);

        // The fake solver honors the bare-jobname contract, so the embedded
        // deck extraction, the spawn and the scoring all run for real; only
        // the physics is fake (its .frd carries no DISP, so no checksum).
        Assert.That(result.Unit, Is.EqualTo(CcxBenchmark.UNIT));
        Assert.That(result.Rate, Is.GreaterThan(0));
        Assert.That(result.Iterations, Is.EqualTo(1));
        Assert.That(result.Elapsed, Is.GreaterThan(TimeSpan.Zero));
        Assert.That(result.Custom, Is.Not.Null);
        Assert.That(result.Custom!["nodes"], Is.EqualTo("8000"));
    }

    [Test]
    public void MeasureFailsLoudlyOnAMissingSolverTest()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"no-ccx-{Guid.NewGuid():N}.exe");

        Assert.That(async () => await CcxBenchmark.MeasureAsync(missing), Throws.Exception);
    }

    #endregion
}
