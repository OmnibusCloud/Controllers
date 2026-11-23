using System.Diagnostics;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Sandbox;

/// <summary>
/// The margin of a session: the host memory one session takes beside the two caps the degree rule
/// counts for it (its linear memory, and the heap of its exception objects). Sessions run side by
/// side, as a package runs them, while the process's memory is sampled; the growth at the peak, a
/// session, less the solver's own linear memory, is what the margin must hold. Two kinds of
/// sessions: small problems, and a problem near the ceiling of a submitted problem's text whose
/// model is megabytes. The numbers are printed: they are the measurement the margin is set from.
/// </summary>
[TestFixture]
[Category("Solver")]
[NonParallelizable]
public sealed class SmtSessionMemoryTests
{
    #region Constants

    private const int SESSIONS = 4;
    private const int LARGE_CONSTANTS = 30_000;
    private const double MEGABYTE = 1024.0 * 1024.0;

    #endregion

    #region Fields

    private string m_folder = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        SmtSolverModule.Require();
        SmtSolverRig.Warm();
        m_folder = Path.Combine(Path.GetTempPath(), $"witai_smt_memory_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_folder);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        SmtRigModules.TryDelete(m_folder);
    }

    #endregion

    #region Margin Tests

    [Test]
    public void SmallSessionsTakeLessThanTheMarginBesideTheirCapsTest()
    {
        var problems = Enumerable.Range(0, 4 * SESSIONS)
            .Select(index => SmtGoldenPackages.Indexed(SmtSolverProblems.Enumerate($"queens-{index}", SmtSolverProblems.Queens(7), 1_000, SmtSolverProblems.QueenNames(7)), index))
            .ToList();

        var overhead = Measure("small", problems);

        Assert.That(overhead, Is.LessThan(SmtNodeRig.SessionMarginBytes), "a small session's host memory beside its linear memory");
    }

    [Test]
    public void SessionsWithModelsOfMegabytesTakeLessThanTheMarginBesideTheirCapsTest()
    {
        var text = SmtSolverProblems.ManyConstants(LARGE_CONSTANTS);
        var problems = Enumerable.Range(0, SESSIONS)
            .Select(index => SmtGoldenPackages.Indexed(SmtSolverProblems.Check($"large-{index}", text), index))
            .ToList();

        var overhead = Measure("a model of megabytes", problems);

        Assert.That(overhead, Is.LessThan(SmtNodeRig.SessionMarginBytes), "a session with a problem of megabytes and a model of megabytes");
    }

    [Test]
    public void SessionsThatWriteUpToTheirOutputCapTakeLessThanTheMarginBesideTheirCapsTest()
    {
        // The same problem of megabytes, enumerated: one model of it after another until the
        // session has written what it may (sixteen MiB), the worst a session keeps of its output.
        var text = SmtSolverProblems.ManyConstants(LARGE_CONSTANTS);
        var problems = Enumerable.Range(0, SESSIONS)
            .Select(index => SmtGoldenPackages.Indexed(SmtSolverProblems.Enumerate($"cap-{index}", text, 12, "some_constant_with_a_long_name_0"), index))
            .ToList();

        var overhead = Measure("the output cap", problems);

        Assert.That(overhead, Is.LessThan(SmtNodeRig.SessionMarginBytes), "a session with a problem of megabytes that writes up to its output cap");
    }

    #endregion

    #region Tools

    /// <summary>Runs the problems as one package at <see cref="SESSIONS"/> sessions at once and gives the peak growth a session beside its linear memory.</summary>
    private double Measure(string what, List<SmtProblemData> problems)
    {
        var path = Path.Combine(m_folder, $"{Guid.NewGuid():N}.wsp");
        var package = SmtNodeRig.Write(problems, new SmtOptionsData(), path);

        // Once to load and warm everything the run touches, then measured.
        SmtNodeRig.RunPackage(package, path, SESSIONS, path + ".warm");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var baseline = SmtMemoryReading.Now().Taken;
        var managedBaseline = GC.GetTotalMemory(forceFullCollection: false);

        // Two readings: the process's memory, which holds the solvers' linear memory where the
        // system has it committed, and the managed heap that is alive, which holds what the host
        // keeps of a session (its output, the answers as text, the problem as the sanitizer took
        // it, the row). The heap is collected before each reading: garbage is no session's.
        long peak = baseline;
        long managedPeak = managedBaseline;
        using var stop = new CancellationTokenSource();
        var sampler = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                managedPeak = Math.Max(managedPeak, GC.GetTotalMemory(forceFullCollection: true));
                peak = Math.Max(peak, SmtMemoryReading.Now().Taken);
                Thread.Sleep(5);
            }
        }) { IsBackground = true };
        sampler.Start();

        var clock = Stopwatch.StartNew();
        var rows = SmtNodeRig.RunPackage(package, path, SESSIONS, path + ".rows");
        stop.Cancel();
        sampler.Join();

        var linear = rows.Average(row => (double)row.PeakMemoryBytes);
        var models = rows.Average(row => row.Models.Sum(model => (double)model.Length));
        var growth = (peak - baseline) / (double)SESSIONS;
        var managed = (managedPeak - managedBaseline) / (double)SESSIONS;
        var overhead = Math.Max(growth - linear, managed);

        TestContext.Out.WriteLine(
            $"{what}: {rows.Count} problems at {SESSIONS} sessions in {clock.ElapsedMilliseconds} ms ({string.Join(", ", rows.Select(SmtNodeRig.Words).Distinct())}); "
            + $"text {problems.Average(problem => (double)problem.Text.Length) / MEGABYTE:F2} MB, models {models / MEGABYTE:F2} MB; "
            + $"a session: process growth {growth / MEGABYTE:F1} MB, linear memory {linear / MEGABYTE:F1} MB, managed heap {managed / MEGABYTE:F1} MB; "
            + $"beside the linear memory {overhead / MEGABYTE:F1} MB; the margin is {SmtNodeRig.SessionMarginBytes / MEGABYTE:F0} MB");

        return overhead;
    }

    #endregion
}
