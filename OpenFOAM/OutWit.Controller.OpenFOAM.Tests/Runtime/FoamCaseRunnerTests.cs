using System.Diagnostics;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Runtime;

[TestFixture]
public class FoamCaseRunnerTests
{
    private const string LEAK_VARIABLE = "OUTWIT_FOAM_TEST_LEAK";

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

    private FakeKit RequireFakeKit()
    {
        var solutionRoot = OpenFOAMTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var fakeFoam = OpenFOAMTestPaths.FindFakeFoamPath(solutionRoot);
        if (fakeFoam == null)
            Assert.Ignore("fake-foam not built");

        m_kit = FakeKit.Create(fakeFoam, "blockMesh", "simpleFoam", "decomposePar", "reconstructPar");
        return m_kit;
    }

    private FoamKit RequireKit()
    {
        return RequireFakeKit().Resolve();
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
    }

    [Test]
    public async Task TheSolverInheritsNothingFromTheNodeProcessTest()
    {
        var kit = RequireKit();
        File.WriteAllText(Path.Combine(m_case, "system", "fake"), "FAKE-ECHO\nITERATIONS=1\n");
        var runner = new FoamCaseRunner(kit, m_case, kit.EnvironmentFor(m_scratch), ranks: 1);

        // A variable of the test process (standing in for the node service's
        // environment) must not reach the solver: the process gets KIT.env
        // resolved and the system PATH, nothing else.
        Environment.SetEnvironmentVariable(LEAK_VARIABLE, "leaked");
        try
        {
            var report = await runner.RunAsync(new FoamRecipeData { Application = "simpleFoam", Steps = [new FoamStepData { Utility = "simpleFoam" }] });

            Assert.That(report.Succeeded, Is.True);
            var log = File.ReadAllText(Path.Combine(m_case, "log.simpleFoam"));
            Assert.That(log, Does.Contain("ENV WM_PROJECT_DIR="));
            Assert.That(log, Does.Not.Contain($"ENV {LEAK_VARIABLE}="));
            Assert.That(log, Does.Not.Contain("ENV FOAM_CASE="), "the case is the working directory, never a variable the controller sets");
        }
        finally
        {
            Environment.SetEnvironmentVariable(LEAK_VARIABLE, null);
        }
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
        Assert.That(report.LogPaths, Has.Count.EqualTo(2), "a skipped step writes no log");
    }

    [Test]
    public async Task WithALauncherParallelStepsGoThroughItOverTheWrittenDecompositionTest()
    {
        var kit = RequireFakeKit().ResolveWithLauncher();
        Assume.That(kit.SupportsParallel, Is.True);
        File.WriteAllText(Path.Combine(m_case, "system", "fake"), "ITERATIONS=2\n");
        var runner = new FoamCaseRunner(kit, m_case, kit.EnvironmentFor(m_scratch), ranks: 4);

        var report = await runner.RunAsync(Recipe(parallel: true));

        Assert.That(report.Succeeded, Is.True, report.LogTail);
        Assert.That(report.Steps.Select(step => step.Ranks), Is.EqualTo(new[] { 1, 1, 4, 1 }), "only the parallel step runs on the ranks; the decomposition steps run serially");
        Assert.That(File.ReadAllText(Path.Combine(m_case, "system", "decomposeParDict")), Does.Contain("numberOfSubdomains 4;").And.Contain("method scotch;"));

        // The fake launcher prints the command line it was given.
        var solverLog = File.ReadAllText(Path.Combine(m_case, "log.simpleFoam"));
        var rankFlag = OperatingSystem.IsWindows() ? "-n 4" : "-np 4";
        Assert.That(solverLog, Does.Contain($"Args   : {rankFlag} {kit.ExecutablePath("simpleFoam")} -parallel"));
        Assert.That(File.ReadAllText(Path.Combine(m_case, "log.decomposePar")), Does.Not.Contain("-parallel"));
    }

    [Test]
    public void OnLinuxAndMacOsTheKitsOwnLauncherIsFoundOnItsPathTest()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("on Windows the launcher is the node's mpiexec, found through MSMPI_BIN");

        var fake = RequireFakeKit();
        Assert.That(fake.Resolve().SupportsParallel, Is.False);

        var withLauncher = fake.ResolveWithLauncher();
        var found = FoamKit.FindMpiLauncher(withLauncher.Environment, withLauncher.Root);

        Assert.That(found, Is.EqualTo(withLauncher.MpiLauncher), "mpirun in a PATH entry of KIT.env is the kit's launcher");
    }

    [Test]
    public async Task ARepeatedUtilityKeepsEveryLogAndTheReportNamesThemTest()
    {
        var kit = RequireKit();
        File.WriteAllText(Path.Combine(m_case, "system", "fake"), "ITERATIONS=2\n");
        var runner = new FoamCaseRunner(kit, m_case, kit.EnvironmentFor(m_scratch), ranks: 1);
        var recipe = new FoamRecipeData
        {
            Application = "simpleFoam",
            Steps =
            [
                new FoamStepData { Utility = "blockMesh" },
                new FoamStepData { Utility = "simpleFoam" },
                new FoamStepData { Utility = "simpleFoam", Arguments = ["-postProcess", "-latestTime"] }
            ]
        };

        var report = await runner.RunAsync(recipe);

        Assert.That(report.Succeeded, Is.True);
        Assert.That(report.LogPaths.Select(Path.GetFileName), Is.EqualTo(new[] { "log.blockMesh", "log.simpleFoam", "log.simpleFoam.2" }));

        var solve = report.LogPathOf(step => step.Utility == "simpleFoam" && !step.Arguments.Contains("-postProcess"));
        var post = report.LogPathOf(step => step.Arguments.Contains("-postProcess"));
        Assert.That(Path.GetFileName(solve), Is.EqualTo("log.simpleFoam"));
        Assert.That(Path.GetFileName(post), Is.EqualTo("log.simpleFoam.2"));
        Assert.That(File.ReadAllText(post!), Does.Contain("-postProcess"));
        Assert.That(File.ReadAllText(solve!), Does.Not.Contain("-postProcess"), "the solve's log is not overwritten by the post step's");
        Assert.That(report.LogPathOf(step => step.Utility == "checkMesh"), Is.Null);
    }

    [Test]
    public void LogNamesCountRepeatsPerUtilityTest()
    {
        var counts = new Dictionary<string, int>();

        Assert.That(FoamCaseRunner.LogName("simpleFoam", counts), Is.EqualTo("log.simpleFoam"));
        Assert.That(FoamCaseRunner.LogName("blockMesh", counts), Is.EqualTo("log.blockMesh"));
        Assert.That(FoamCaseRunner.LogName("simpleFoam", counts), Is.EqualTo("log.simpleFoam.2"));
        Assert.That(FoamCaseRunner.LogName("simpleFoam", counts), Is.EqualTo("log.simpleFoam.3"));
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
        Assert.That(report.LogPaths, Has.Count.EqualTo(1));
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
        var fake = RequireFakeKit();
        var step = new FoamStepData { Utility = "simpleFoam", Arguments = ["-postProcess"], Parallel = true };

        var serialKit = fake.Resolve();
        var serial = new FoamCaseRunner(serialKit, m_case, serialKit.EnvironmentFor(m_scratch), ranks: 6).CommandLine(step, parallel: false);
        Assert.That(serial.FileName, Is.EqualTo(serialKit.ExecutablePath("simpleFoam")));
        Assert.That(serial.Arguments, Is.EqualTo(new[] { "-postProcess" }));

        // Without a launcher the parallel form falls back to the serial one.
        var fallback = new FoamCaseRunner(serialKit, m_case, serialKit.EnvironmentFor(m_scratch), ranks: 6).CommandLine(step, parallel: true);
        Assert.That(fallback.FileName, Is.EqualTo(serialKit.ExecutablePath("simpleFoam")));
        Assert.That(fallback.Arguments, Does.Not.Contain("-parallel"));

        // With one, the launcher runs the solver on the ranks with -parallel appended.
        var parallelKit = fake.ResolveWithLauncher();
        var parallel = new FoamCaseRunner(parallelKit, m_case, parallelKit.EnvironmentFor(m_scratch), ranks: 6).CommandLine(step, parallel: true);
        Assert.That(parallel.FileName, Is.EqualTo(parallelKit.MpiLauncher));
        Assert.That(parallel.Arguments, Is.EqualTo(new[] { OperatingSystem.IsWindows() ? "-n" : "-np", "6", parallelKit.ExecutablePath("simpleFoam"), "-postProcess", "-parallel" }));
    }

    #endregion
}
