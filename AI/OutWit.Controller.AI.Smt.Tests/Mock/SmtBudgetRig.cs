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

    /// <summary>A budget of a machine with given numbers whose sessions run on threads the test makes: a machine that may have no thread to give.</summary>
    /// <param name="cores">The machine's logical cores.</param>
    /// <param name="availableBytes">The memory it has available; 0 when it cannot say.</param>
    /// <param name="makeThread">Makes the thread of a session, not started, or throws.</param>
    /// <returns>The budget.</returns>
    public static object Budget(int cores, long availableBytes, Func<Action, Thread> makeThread)
    {
        return new SmtSessionBudget(() => (cores, availableBytes), SmtSessionBudget.MAX_OPEN_PACKAGES, makeThread);
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
    /// <param name="handedOut">Called when the package has given its problems to the budget (the budget has started every session it may start for them now), before its rows are waited for; null for nothing.</param>
    /// <returns>Its rows and its degree, or the code of its refusal, or that it was cancelled.</returns>
    public static async Task<SmtBudgetRun> SolveAsync(object budget, SmtTaskPackageData package, string problemsPath, string resultsPath,
        SmtScriptedSolver solver, CancellationToken cancellationToken, Action? handedOut = null)
    {
        // What is awaited here is a task as a plain task, never as a task of a controller type: its
        // awaiter would be a field of this method's state machine, a value type of the tests. The
        // result is read in a method body once the task has ended.
        var entering = EnterAsync(budget, cancellationToken);
        try
        {
            await entering;
        }
        catch (OperationCanceledException)
        {
            return new SmtBudgetRun { Cancelled = true };
        }

        var turn = ResultOf(entering);

        try
        {
            var (degree, reason, fault) = Prepare(turn, package);
            if (fault != null)
                return new SmtBudgetRun { Fault = fault };

            var running = RunAsync(turn, package, problemsPath, resultsPath, solver, cancellationToken);
            handedOut?.Invoke();
            await running;
            return new SmtBudgetRun { Rows = SmtNodeRig.ReadRows(WrittenPath(running), package.ProblemCount), Degree = degree, Reason = reason };
        }
        catch (OperationCanceledException)
        {
            return new SmtBudgetRun { Cancelled = true, ResultsLeft = File.Exists(resultsPath) };
        }
        catch (Exception exception) when (RefusalCode(exception) is not null)
        {
            return new SmtBudgetRun { Fault = RefusalCode(exception) };
        }
        finally
        {
            ((IDisposable)turn).Dispose();
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

    /// <summary>Runs work on the controller's pool of worker threads, each made by the test: a machine that may have no thread to give.</summary>
    /// <param name="count">The work items.</param>
    /// <param name="degree">The workers at once.</param>
    /// <param name="body">The work of one index.</param>
    /// <param name="makeThread">Makes a worker's thread, not started, or throws.</param>
    public static void Pool(int count, int degree, Action<int> body, Func<Action, Thread> makeThread)
    {
        SmtWorkerPool.Run(count, degree, body, makeThread);
    }

    /// <summary>The node's own budget, the one <c>Smt.SolvePackage</c> runs on, with its highest numbers forgotten.</summary>
    /// <returns>The budget.</returns>
    public static object Node()
    {
        SmtSessionBudget.Node.ResetPeaks();
        return SmtSessionBudget.Node;
    }

    private static Task EnterAsync(object budget, CancellationToken cancellationToken)
    {
        return ((SmtSessionBudget)budget).EnterAsync(cancellationToken);
    }

    private static object ResultOf(Task entering)
    {
        return ((Task<SmtSessionBudgetTurn>)entering).Result;
    }

    private static string WrittenPath(Task running)
    {
        return ((Task<SmtWrittenResults>)running).Result.Path;
    }

    private static (int Degree, string Reason, string? Fault) Prepare(object turn, SmtTaskPackageData package)
    {
        try
        {
            var degree = SmtPackageRunner.Prepare(package, null, ((SmtSessionBudgetTurn)turn).Degree);
            return (degree.Degree, degree.Reason, null);
        }
        catch (SmtPackageException refused)
        {
            return (0, "", refused.Code);
        }
    }

    private static Task RunAsync(object turn, SmtTaskPackageData package, string problemsPath, string resultsPath, SmtScriptedSolver solver, CancellationToken cancellationToken)
    {
        var sessionBytes = SmtSandboxDegree.SessionBytes(package.Options!);
        return SmtPackageRunner.RunAsync((SmtSessionBudgetTurn)turn, package, problemsPath, (problem, _, token) =>
            {
                var (milliseconds, cancelled) = solver.Session(package.Ticket, problem.Id, sessionBytes, token);
                return cancelled
                    ? new SmtSolveOutcome { Status = SmtSolveStatus.NodeFault, Code = SmtSolveCodes.CANCELLED, Message = "the session was cancelled", Query = problem.Query }
                    : new SmtSolveOutcome { Status = SmtSolveStatus.Ok, Query = problem.Query, Verdict = SmtVerdict.Sat, Fuel = milliseconds, WallMs = milliseconds };
            }, resultsPath, null, NullLogger.Instance, cancellationToken);
    }

    private static string? RefusalCode(Exception exception)
    {
        return exception is SmtPackageException refused ? refused.Code : null;
    }

    #endregion

    #region Properties

    /// <summary>The most packages open in a process at once.</summary>
    public static int MaxOpenPackages => SmtSessionBudget.MAX_OPEN_PACKAGES;

    #endregion
}
