using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Sandbox;
using OutWit.Controller.AI.Smt.Solver;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// How the tests meet the node's budget of sessions: a budget of a machine of given numbers, and a
/// package solved on it as a node solves one (its turn waited for, its degree read on the budget,
/// its problems read from its file one at a time, each row made by the node's code and written to
/// its own package of results) by a scripted solver (<see cref="SmtScriptedSolver"/>), so that the
/// time a problem takes is the test's. As in <see cref="SmtNodeRig"/>, the controller's types are
/// met inside method bodies only: a budget travels as an object.
/// </summary>
internal static class SmtBudgetRig
{
    #region Functions

    /// <summary>A budget of a machine with given numbers.</summary>
    /// <param name="cores">The machine's logical cores.</param>
    /// <param name="availableBytes">The memory it has available; 0 when it cannot say.</param>
    /// <param name="maxOpen">The most packages open at once; 0 for the controller's.</param>
    /// <returns>The budget.</returns>
    public static object Budget(int cores, long availableBytes, int maxOpen = 0)
    {
        return Budget(() => (cores, availableBytes), maxOpen);
    }

    /// <summary>A budget of a machine read by the test's function.</summary>
    /// <param name="read">Gives the machine's cores and available memory each time the budget reads it.</param>
    /// <param name="maxOpen">The most packages open at once; 0 for the controller's.</param>
    /// <returns>The budget.</returns>
    public static object Budget(Func<(int Cores, long AvailableBytes)> read, int maxOpen = 0)
    {
        return new SmtSessionBudget(read, maxOpen > 0 ? maxOpen : SmtSessionBudget.MAX_OPEN_PACKAGES);
    }

    /// <summary>
    /// Solves a package on a budget as a node does, its problems by the scripted solver: each row
    /// says <c>ok sat</c> with the script's milliseconds as its fuel, or is the node's fault
    /// <c>cancelled</c> when the cancel ended its session.
    /// </summary>
    /// <param name="budget">The budget.</param>
    /// <param name="package">The package's reference.</param>
    /// <param name="problemsPath">Its file.</param>
    /// <param name="resultsPath">The file its rows are written to.</param>
    /// <param name="solver">The scripted solver.</param>
    /// <param name="cancellationToken">The package's job's token.</param>
    /// <returns>Its rows and its degree, or the code of its refusal, or that it was cancelled.</returns>
    public static async Task<SmtBudgetRun> SolveAsync(object budget, SmtTaskPackageData package, string problemsPath, string resultsPath,
        SmtScriptedSolver solver, CancellationToken cancellationToken)
    {
        SmtSessionBudgetTurn turn;
        try
        {
            turn = await ((SmtSessionBudget)budget).EnterAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new SmtBudgetRun { Cancelled = true };
        }

        try
        {
            var degree = SmtPackageRunner.Prepare(package, null, turn.Degree);
            var sessionBytes = SmtSandboxDegree.SessionBytes(package.Options!);

            var written = await SmtPackageRunner.RunAsync(turn, package, problemsPath, (problem, _, token) =>
            {
                var (milliseconds, cancelled) = solver.Session(package.Ticket, problem.Id, sessionBytes, token);
                return cancelled
                    ? new SmtSolveOutcome { Status = SmtSolveStatus.NodeFault, Code = SmtSolveCodes.CANCELLED, Message = "the session was cancelled", Query = problem.Query }
                    : new SmtSolveOutcome { Status = SmtSolveStatus.Ok, Query = problem.Query, Verdict = SmtVerdict.Sat, Fuel = milliseconds, WallMs = milliseconds };
            }, resultsPath, null, NullLogger.Instance, cancellationToken);

            return new SmtBudgetRun { Rows = SmtNodeRig.ReadRows(written.Path, package.ProblemCount), Degree = degree.Degree, Reason = degree.Reason };
        }
        catch (SmtPackageException refused)
        {
            return new SmtBudgetRun { Fault = refused.Code };
        }
        catch (OperationCanceledException)
        {
            return new SmtBudgetRun { Cancelled = true, ResultsLeft = File.Exists(resultsPath) };
        }
        finally
        {
            turn.Dispose();
        }
    }

    /// <summary>What a budget counted: its sessions and packages now, the most at once, and how often it read the machine.</summary>
    /// <param name="budget">The budget.</param>
    /// <returns>The numbers.</returns>
    public static (int Running, int Open, int Waiting, int Workers, int PeakRunning, long PeakRunningBytes, int PeakOpen, int Reads, int SessionCap, long CapacityBytes) Counts(object budget)
    {
        var counted = (SmtSessionBudget)budget;
        return (counted.Running, counted.Open, counted.Waiting, counted.Workers, counted.PeakRunning, counted.PeakRunningBytes, counted.PeakOpen,
            counted.Reads, counted.SessionCap, counted.CapacityBytes);
    }

    /// <summary>The node's own budget, the one <c>Smt.SolvePackage</c> runs on, with its highest numbers forgotten.</summary>
    /// <returns>The budget.</returns>
    public static object Node()
    {
        SmtSessionBudget.Node.ResetPeaks();
        return SmtSessionBudget.Node;
    }

    #endregion

    #region Properties

    /// <summary>The most packages open in a process at once.</summary>
    public static int MaxOpenPackages => SmtSessionBudget.MAX_OPEN_PACKAGES;

    #endregion
}
