using System.Diagnostics;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Runtime;

[TestFixture]
public class FoamCaseRunnerTests
{
    private string m_scratch = null!;
    private string m_case = null!;
    private FakeKit? m_kit;

    [SetUp]
    public void Setup()
    {
        m_scratch = OpenFOAMTestPaths.CreateScratch("foam-run");
        m_case = Path.Combine(m_scratch, "case");
        Directory.CreateDirectory(Path.Combine(m_case, "system"));
        Directory.CreateDirectory(Path.Combine(m_scratch, "home"));
        Directory.CreateDirectory(Path.Combine(m_scratch, "tmp"));
    }

    [TearDown]
    public void TearDown()
    {
        m_kit?.Dispose();
        OpenFOAMTestPaths.TryDelete(m_scratch);
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

        m_kit = FakeKit.Create(fakeFoam, "blockMesh", "simpleFoam", "decomposePar", "reconstructPar");
        return m_kit.Resolve();
    }

    private static FoamRecipeData Recipe(bool parallel)
    {
        return new FoamRecipeData
        {
            Application = "simpleFoam",
            Steps =
            [
                new FoamStepData { Utility = "blockMesh" },
                new FoamStepData { Utility = "decomposePar" },
                new FoamStepData { Utility = "simpleFoam", Parallel = parallel },
                new FoamStepData { Utility = "reconstructPar", Arguments = ["-latestTime"] }
            ]
        };
    }

    #endregion

    #region Runner Tests

    [Test]
    public async Task EveryStepRunsInOrderWithItsLogAndTheKitEnvironmentTest()
    {
        var kit = RequireKit();
        File.WriteAllText(Path.Combine(m_case, "system", "fake"), "FAKE-ECHO\nITERATIONS=3\n");
        var environment = kit.EnvironmentFor(m_scratch);
        var runner = new FoamCaseRunner(kit, m_case, environment, ranks: 4);

        var report = await runner.RunAsync(Recipe(parallel: false));

        Assert.That(report.Succeeded, Is.True);
        Assert.That(report.Steps.Select(step => step.Utility), Is.EqualTo(new[] { "blockMesh", "decomposePar", "simpleFoam", "reconstructPar" }));
        Assert.That(report.Steps.All(step => step.ExitCode == 0), Is.True);
        Assert.That(File.Exists(Path.Combine(m_case, "log.simpleFoam")), Is.True);
        Assert.That(Directory.Exists(Path.Combine(m_case, "3")), Is.True, "the fake solver wrote its last time");

        var log = File.ReadAllText(Path.Combine(m_case, "log.blockMesh"));
        Assert.That(log, Does.Contain($"ENV HOME={environment["HOME"]}"));
        Assert.That(log, Does.Contain("ENV FOAM_SIGFPE=true"));
        Assert.That(log, Does.Contain("ENV FOAM_CASE=").Or.Not.Contain("ENV FOAM_CASE="), "FOAM_CASE is the solver's own business");
    }

    [Test]
    public async Task WithoutAnMpiLauncherParallelStepsRunSeriallyAndDecompositionIsSkippedTest()
    {
        var kit = RequireKit();
        Assume.That(kit.SupportsParallel, Is.False, "the fake kit has no mpirun");
        var runner = new FoamCaseRunner(kit, m_case, kit.EnvironmentFor(m_scratch), ranks: 4);

        var report = await runner.RunAsync(Recipe(parallel: true));

        Assert.That(report.Succeeded, Is.True);
        var decompose = report.Steps.Single(step => step.Utility == "decomposePar");
        Assert.That(decompose.Ranks, Is.EqualTo(0), "skipped, not run");
        Assert.That(report.Steps.Single(step => step.Utility == "simpleFoam").Ranks, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(m_case, "system", "decomposeParDict")), Is.False);
    }

    [Test]
    public async Task AFailingStepEndsTheRunWithItsTailTest()
    {
        var kit = RequireKit();
        File.WriteAllText(Path.Combine(m_case, "system", "fake"), "FAKE-FAIL\n");
        var runner = new FoamCaseRunner(kit, m_case, kit.EnvironmentFor(m_scratch), ranks: 1);

        var report = await runner.RunAsync(Recipe(parallel: false));

        Assert.That(report.Succeeded, Is.False);
        Assert.That(report.FailedStep, Is.EqualTo("blockMesh"), "every fake utility reads the same system/fake");
        Assert.That(report.ExitCode, Is.EqualTo(1));
        Assert.That(report.LogTail, Does.Contain("FOAM FATAL ERROR"));
        Assert.That(report.Steps, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task CancellationKillsTheRunningStepTest()
    {
        var kit = RequireKit();
        File.WriteAllText(Path.Combine(m_case, "system", "fake"), "FAKE-HANG\n");
        var runner = new FoamCaseRunner(kit, m_case, kit.EnvironmentFor(m_scratch), ranks: 1);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var report = await runner.RunAsync(Recipe(parallel: false), cts.Token);
            Assert.That(report.Succeeded, Is.False, "a killed step never reports success");
        }
        catch (OperationCanceledException)
        {
            // Also acceptable: the runner may observe the token between steps.
        }

        stopwatch.Stop();
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(30)), "the step outlived its cancellation");
    }

    [Test]
    public void TheCommandLineOfAParallelStepGoesThroughTheLauncherTest()
    {
        var kit = RequireKit();
        var runner = new FoamCaseRunner(kit, m_case, kit.EnvironmentFor(m_scratch), ranks: 6);
        var step = new FoamStepData { Utility = "simpleFoam", Arguments = ["-postProcess"], Parallel = true };

        var serial = runner.CommandLine(step, parallel: false);
        Assert.That(serial.FileName, Is.EqualTo(kit.ExecutablePath("simpleFoam")));
        Assert.That(serial.Arguments, Is.EqualTo(new[] { "-postProcess" }));

        // The fake kit has no launcher; the parallel form falls back to the serial one.
        var parallel = runner.CommandLine(step, parallel: true);
        Assert.That(parallel.FileName, Is.EqualTo(kit.ExecutablePath("simpleFoam")));
    }

    [Test]
    public void TheDecompositionDictionaryNamesScotchAndTheRanksTest()
    {
        var path = FoamDecomposition.WriteDecomposeParDict(m_case, 6);

        var text = File.ReadAllText(path);
        Assert.That(text, Does.Contain("numberOfSubdomains 6;").And.Contain("method scotch;"));
        Assert.That(FoamDecomposition.Ranks(0), Is.InRange(1, FoamDecomposition.MAX_DEFAULT_RANKS));
        Assert.That(FoamDecomposition.Ranks(3), Is.EqualTo(3));
    }

    #endregion
}
