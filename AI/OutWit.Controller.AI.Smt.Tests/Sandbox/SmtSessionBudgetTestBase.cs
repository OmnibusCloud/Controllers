using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Sandbox;

/// <summary>
/// The ground of the tests of the node's budget of sessions: packages written to files of the
/// test's own, solved on a budget of a machine of given numbers by a scripted solver
/// (<see cref="SmtBudgetRig"/>), all of a share started together as the client starts them, and
/// the checks every such run holds to: each package returned exactly its own rows, under its
/// ticket, each digest holding, and the budget ended idle.
/// </summary>
public abstract class SmtSessionBudgetTestBase
{
    #region Constants

    private protected const long MEGABYTE = 1L << 20;

    private protected const long GIGABYTE = 1L << 30;

    #endregion

    #region Fields

    private string m_folder = null!;

    private int m_files;

    #endregion

    #region Setup

    [SetUp]
    public void SetupFolder()
    {
        m_folder = Path.Combine(Path.GetTempPath(), $"witai_smt_budget_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_folder);
    }

    [TearDown]
    public void TearDownFolder()
    {
        SmtRigModules.TryDelete(m_folder);
    }

    #endregion

    #region Tools

    /// <summary>Writes a package of problems named <c>name-0</c>, <c>name-1</c>, ... to a file of the test's own.</summary>
    /// <param name="name">The package's name: the start of its problems' ids.</param>
    /// <param name="count">Its problems.</param>
    /// <param name="options">Its options; the default when null.</param>
    /// <returns>The package.</returns>
    private protected SmtBudgetPackage Package(string name, int count, SmtOptionsData? options = null)
    {
        var problems = Enumerable.Range(0, count)
            .Select(index => SmtGoldenPackages.Indexed(SmtSolverProblems.Check($"{name}-{index}", $"(set-logic QF_LIA)(declare-const x Int)(assert (> x {index}))"), 100 * m_files + index))
            .ToList();

        var path = Path.Combine(m_folder, $"{name}-{m_files++}.wsp");
        var package = SmtNodeRig.Write(problems, options ?? new SmtOptionsData(), path);
        return new SmtBudgetPackage(name, package, path, Path.Combine(m_folder, $"{name}-{m_files}.rows"), problems);
    }

    /// <summary>Starts every package together on a budget, as the client starts a share, and waits for all of them.</summary>
    /// <param name="budget">The budget.</param>
    /// <param name="solver">The scripted solver.</param>
    /// <param name="packages">The packages, in the order they ask for their turn.</param>
    /// <returns>What each came to, in the packages' order.</returns>
    private protected static Task<SmtBudgetRun[]> SolveAllAsync(object budget, SmtScriptedSolver solver, params SmtBudgetPackage[] packages)
    {
        return SolveAllAsync(budget, solver, packages.Select(package => (package, CancellationToken.None)).ToArray());
    }

    /// <summary>Starts every package together on a budget, each with its job's token, and waits for all of them.</summary>
    /// <param name="budget">The budget.</param>
    /// <param name="solver">The scripted solver.</param>
    /// <param name="packages">The packages and their jobs' tokens, in the order they ask for their turn.</param>
    /// <returns>What each came to, in the packages' order.</returns>
    private protected static Task<SmtBudgetRun[]> SolveAllAsync(object budget, SmtScriptedSolver solver, params (SmtBudgetPackage Package, CancellationToken Token)[] packages)
    {
        // Each asks for its turn in its order before the next one does: the order the budget
        // keeps is the order they came in.
        var runs = new List<Task<SmtBudgetRun>>();
        foreach (var (package, token) in packages)
            runs.Add(SmtBudgetRig.SolveAsync(budget, package.Reference, package.ProblemsPath, package.ResultsPath, solver, token));

        return Task.WhenAll(runs);
    }

    /// <summary>A run returned exactly its package's rows: one a problem, its ids and indices, its ticket, each digest holding.</summary>
    /// <param name="run">The run.</param>
    /// <param name="package">The package.</param>
    private protected static void AssertOwnRows(SmtBudgetRun run, SmtBudgetPackage package)
    {
        Assert.Multiple(() =>
        {
            Assert.That(run.Fault, Is.Null, package.Name);
            Assert.That(run.Cancelled, Is.False, package.Name);
            Assert.That(run.Rows.Select(row => row.Id), Is.EquivalentTo(package.Problems.Select(problem => problem.Id)), $"{package.Name}: its own problems, each once");
            Assert.That(run.Rows.Select(row => row.Index), Is.EquivalentTo(package.Problems.Select(problem => problem.Index)), $"{package.Name}: their indices");
            Assert.That(run.Rows.Select(row => row.Ticket), Has.All.EqualTo(package.Reference.Ticket), $"{package.Name}: under its ticket");
            Assert.That(run.Rows.Select(SmtResultDigest.Holds), Has.All.True, $"{package.Name}: every digest holds");
        });
    }

    /// <summary>A budget ended idle: no session running, no package open or waiting, no thread left.</summary>
    /// <param name="budget">The budget.</param>
    private protected static void AssertIdle(object budget)
    {
        var counts = SmtBudgetRig.Counts(budget);
        Assert.Multiple(() =>
        {
            Assert.That(counts.Running, Is.Zero, "sessions running");
            Assert.That(counts.Open, Is.Zero, "packages open");
            Assert.That(counts.Waiting, Is.Zero, "packages waiting");
            Assert.That(counts.Workers, Is.Zero, "session threads");
        });
    }

    #endregion
}
