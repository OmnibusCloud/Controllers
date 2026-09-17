using System.Diagnostics;
using OutWit.Controller.CalculiX.Runtime;
using OutWit.Controller.CalculiX.Tests.Utils;

namespace OutWit.Controller.CalculiX.Tests.Runtime;

[TestFixture]
public class CcxProcessRunnerTests
{
    private string m_jobDirectory = null!;

    [SetUp]
    public void Setup()
    {
        m_jobDirectory = Path.Combine(Path.GetTempPath(), $"ccx-runner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_jobDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(m_jobDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    #region Cancellation Tests

    [Test]
    public void TheSolverStartsWithoutAConsoleWindowAndWithTheHostContractTest()
    {
        // Without CreateNoWindow a console program started by the windowed worker client gets a
        // console of its own - about 0.45 s per ccx start on Windows 11.
        var startInfo = CcxProcessRunner.CreateStartInfo("ccx", "job", m_jobDirectory, 3);

        Assert.That(startInfo.CreateNoWindow, Is.True);
        Assert.That(startInfo.UseShellExecute, Is.False);
        Assert.That(startInfo.RedirectStandardOutput, Is.True);
        Assert.That(startInfo.RedirectStandardError, Is.True);
        Assert.That(startInfo.WorkingDirectory, Is.EqualTo(m_jobDirectory));
        Assert.That(startInfo.ArgumentList, Is.EqualTo(new[] { "job" }));
        Assert.That(startInfo.EnvironmentVariables["OMP_NUM_THREADS"], Is.EqualTo("3"));
    }

    [Test]
    public void ZeroThreadsMeansAllCoresTest()
    {
        var startInfo = CcxProcessRunner.CreateStartInfo("ccx", "job", m_jobDirectory, 0);

        Assert.That(startInfo.EnvironmentVariables["OMP_NUM_THREADS"], Is.EqualTo(Environment.ProcessorCount.ToString()));
    }

    [Test]
    public async Task CancellationKillsARunningSolveTest()
    {
        var solutionRoot = CalculiXTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var fakeCcx = CalculiXTestPaths.FindFakeCcxPath(solutionRoot);
        if (fakeCcx == null)
            Assert.Ignore("fake-ccx not built");

        // A wedged solve: the fake solver sleeps for minutes; only the
        // cancellation kill can end it inside the test budget.
        File.WriteAllText(Path.Combine(m_jobDirectory, "job.inp"), "** FAKE-HANG\n");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var stopwatch = Stopwatch.StartNew();

        var outcome = await CcxProcessRunner.RunAsync(fakeCcx, "job", m_jobDirectory, threads: 1, cts.Token);
        stopwatch.Stop();

        // The kill reached the process tree: the run ended promptly (not
        // after the 5-minute sleep) and the exit code is the kill's, never 0
        // — the adapter's ThrowIfCancellationRequested then turns it into
        // cancellation instead of a red variant.
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(30)),
            "the solve outlived its cancellation");
        Assert.That(outcome.ExitCode, Is.Not.EqualTo(0));
    }

    [Test]
    public async Task AnUncancelledSolveRunsToCompletionTest()
    {
        var solutionRoot = CalculiXTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var fakeCcx = CalculiXTestPaths.FindFakeCcxPath(solutionRoot);
        if (fakeCcx == null)
            Assert.Ignore("fake-ccx not built");

        File.WriteAllText(Path.Combine(m_jobDirectory, "job.inp"), "*HEADING\nplain\n");

        var outcome = await CcxProcessRunner.RunAsync(fakeCcx, "job", m_jobDirectory, threads: 1);

        Assert.That(outcome.ExitCode, Is.EqualTo(0));
        Assert.That(File.Exists(Path.Combine(m_jobDirectory, "job.frd")), Is.True);
        Assert.That(outcome.LogTail, Does.Contain("Job finished"));
    }

    #endregion
}
