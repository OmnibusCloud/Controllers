using System.Diagnostics;
using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Runtime;

[TestFixture]
public class FoamProcessRunnerTests
{
    private string m_case = null!;

    [SetUp]
    public void Setup()
    {
        m_case = OpenFOAMTestPaths.CreateScratch("foam-process");
        Directory.CreateDirectory(Path.Combine(m_case, "system"));
    }

    [TearDown]
    public void TearDown()
    {
        OpenFOAMTestPaths.TryDelete(m_case);
    }

    #region Tools

    private static string RequireFakeFoam()
    {
        var solutionRoot = OpenFOAMTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var fakeFoam = OpenFOAMTestPaths.FindFakeFoamPath(solutionRoot);
        if (fakeFoam == null)
            Assert.Ignore("fake-foam not built");

        return fakeFoam;
    }

    private static Dictionary<string, string> MinimalEnvironment()
    {
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = FoamKitEnvironment.SystemPath(),
            // The fake solver is a .NET apphost; see OpenFOAMTestPaths.DotnetRoot.
            ["DOTNET_ROOT"] = OpenFOAMTestPaths.DotnetRoot(),
            ["HOME"] = Path.GetTempPath(),
            ["FOAM_SIGFPE"] = "true"
        };
        if (OperatingSystem.IsWindows())
            environment["SystemRoot"] = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";

        return environment;
    }

    #endregion

    #region Start Info Tests

    [Test]
    public void TheProcessStartsWithoutAConsoleWindowAndWithTheEnvironmentReplacedWholeTest()
    {
        var environment = MinimalEnvironment();

        var startInfo = FoamProcessRunner.CreateStartInfo("simpleFoam", ["-parallel", "-case", "x"], m_case, environment);

        // Without CreateNoWindow a console program started by the windowed
        // worker client gets a console of its own - about 0.45 s per start.
        Assert.That(startInfo.CreateNoWindow, Is.True);
        Assert.That(startInfo.UseShellExecute, Is.False);
        Assert.That(startInfo.RedirectStandardOutput, Is.True);
        Assert.That(startInfo.RedirectStandardError, Is.True);
        Assert.That(startInfo.WorkingDirectory, Is.EqualTo(m_case));
        Assert.That(startInfo.ArgumentList, Is.EqualTo(new[] { "-parallel", "-case", "x" }));

        // Nothing of the node service's environment leaks into the solver's.
        Assert.That(startInfo.Environment.Count, Is.EqualTo(environment.Count));
        Assert.That(startInfo.Environment["FOAM_SIGFPE"], Is.EqualTo("true"));
        Assert.That(startInfo.Environment.ContainsKey("USERNAME"), Is.False);
    }

    #endregion

    #region Run Tests

    [Test]
    public async Task ARunWritesTheLogFileAndKeepsTheTailTest()
    {
        var fakeFoam = RequireFakeFoam();
        File.WriteAllText(Path.Combine(m_case, "system", "fake"), "ITERATIONS=2\n");
        var log = Path.Combine(m_case, "log.simpleFoam");

        var outcome = await FoamProcessRunner.RunAsync(fakeFoam, [], m_case, MinimalEnvironment(), log);

        Assert.That(outcome.ExitCode, Is.EqualTo(0));
        Assert.That(outcome.ElapsedSeconds, Is.GreaterThan(0));
        Assert.That(outcome.LogTail, Does.Contain("End"));
        Assert.That(File.Exists(log), Is.True);
        Assert.That(File.ReadAllText(log), Does.Contain("Time = 2").And.Contain("SIMPLE solution converged"));
    }

    [Test]
    public async Task AFailingRunForwardsItsExitCodeAndItsErrorTailTest()
    {
        var fakeFoam = RequireFakeFoam();
        File.WriteAllText(Path.Combine(m_case, "system", "fake"), "FAKE-FAIL\n");

        var outcome = await FoamProcessRunner.RunAsync(fakeFoam, [], m_case, MinimalEnvironment(), null);

        Assert.That(outcome.ExitCode, Is.EqualTo(1));
        Assert.That(outcome.LogTail, Does.Contain("FOAM FATAL ERROR").And.Contain("FOAM exiting"), "stderr is merged into the tail");
    }

    [Test]
    public void AStartedProcessRunsBelowNormalPriorityTest()
    {
        var fakeFoam = RequireFakeFoam();
        File.WriteAllText(Path.Combine(m_case, "system", "fake"), "FAKE-HANG\n");

        // A wedged fake solve stays alive long enough to read its priority back.
        using var process = new Process { StartInfo = FoamProcessRunner.CreateStartInfo(fakeFoam, [], m_case, MinimalEnvironment()) };
        process.Start();
        try
        {
            var lowered = FoamProcessRunner.LowerPriority(process);

            Assert.That(lowered, Is.True);
            Assert.That(process.PriorityClass, Is.EqualTo(FoamProcessRunner.RUN_PRIORITY));
        }
        finally
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        }
    }

    [Test]
    public async Task CancellationKillsTheProcessTreeTest()
    {
        var fakeFoam = RequireFakeFoam();
        File.WriteAllText(Path.Combine(m_case, "system", "fake"), "FAKE-HANG\n");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var stopwatch = Stopwatch.StartNew();

        var outcome = await FoamProcessRunner.RunAsync(fakeFoam, [], m_case, MinimalEnvironment(), null, cts.Token);
        stopwatch.Stop();

        // The kill reached the process: the run ended promptly (not after the
        // 5-minute sleep) and the exit code is the kill's, never 0.
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(30)), "the process outlived its cancellation");
        Assert.That(outcome.ExitCode, Is.Not.EqualTo(0));
    }

    [Test]
    public void AMissingExecutableFailsLoudlyTest()
    {
        var missing = Path.Combine(m_case, OperatingSystem.IsWindows() ? "noFoam.exe" : "noFoam");

        Assert.That(async () => await FoamProcessRunner.RunAsync(missing, [], m_case, MinimalEnvironment(), null), Throws.Exception);
    }

    #endregion
}
