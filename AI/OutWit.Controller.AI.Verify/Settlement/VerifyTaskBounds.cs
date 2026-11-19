using OutWit.Controller.AI.Verify.Delivery;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Settlement;

/// <summary>
/// What the results of one task package are held to: the tasks of the package as the server
/// stored them. The server has the task, so a result is not only something a node may say but
/// something a node may say of that task: a number of fuel no larger than the task's budget
/// allows its runs, a peak of memory no larger than its cap, no more stdout than its cap, a case
/// record for each case of its suite and for no other. A node on this controller version runs a
/// task under the same limits (<see cref="VerifyLimitCeilings.Effective"/>), so an honest result
/// is always inside them.
/// <para>
/// Stderr is held to the ceiling only: it is also where a node puts what the engine or the
/// compiler said of a run that never started (a module that did not compile, a build that
/// failed), and that is not counted against a task's cap.
/// </para>
/// </summary>
internal sealed class VerifyTaskBounds
{
    #region Fields

    private readonly Dictionary<int, VerifyTaskBound> m_tasks;

    #endregion

    #region Constructors

    private VerifyTaskBounds(string ticket, Dictionary<int, VerifyTaskBound> tasks)
    {
        Ticket = ticket;
        m_tasks = tasks;
    }

    #endregion

    #region Functions

    /// <summary>Reads the bounds of a task package from the server's own blob.</summary>
    /// <param name="unit">The task package.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <returns>The bounds of the package's tasks.</returns>
    /// <exception cref="InvalidDataException">The blob is not the package the reference names.</exception>
    public static async Task<VerifyTaskBounds> LoadAsync(VerifyTaskPackageData unit, IWitBlobService blobs)
    {
        return Of(unit.Ticket, await VerifyTaskPackages.LoadAsync(unit, blobs));
    }

    /// <summary>Makes the bounds of the tasks of a package.</summary>
    /// <param name="ticket">The package's ticket.</param>
    /// <param name="batches">The package's task batches.</param>
    /// <returns>The bounds.</returns>
    public static VerifyTaskBounds Of(string ticket, IEnumerable<VerifyTaskBatchData> batches)
    {
        var tasks = new Dictionary<int, VerifyTaskBound>();

        foreach (var batch in batches)
        {
            foreach (var task in batch.Tasks)
            {
                // The limits a node runs the task under: resolved and clamped the same way.
                var limits = VerifyLimitCeilings.Effective(task.Limits, batch.DefaultLimits);
                var cases = task.Suite?.Cases?.Count ?? 0;

                // A task runs once a case, each run under the whole budget.
                var runs = Math.Max(1, cases);
                var fuel = limits.FuelBudget > long.MaxValue / runs ? long.MaxValue : limits.FuelBudget * runs;

                tasks[task.TaskIndex] = new VerifyTaskBound(fuel, limits.MemoryBytes, limits.StdoutLimitBytes, cases);
            }
        }

        return new VerifyTaskBounds(ticket, tasks);
    }

    /// <summary>
    /// Whether a result is within what its task allows. The result is one a node may give
    /// (<see cref="VerifyRoundIntake"/> asks that first): its texts and its case records are there.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns>False for a result of a task the package does not have, or one outside the task's limits.</returns>
    public bool Holds(VerifyResultData result)
    {
        if (!m_tasks.TryGetValue(result.TaskIndex, out var bound))
            return false;

        if (result.FuelConsumed < 0 || result.FuelConsumed > bound.Fuel)
            return false;

        if (result.PeakMemoryBytes < 0 || result.PeakMemoryBytes > bound.MemoryBytes)
            return false;

        if (result.WallMs < 0)
            return false;

        if (result.Stdout.Length > bound.OutputCharacters)
            return false;

        // A record for each case, in the suite's order; none at all for a task that never ran.
        var cases = result.CaseResults;
        if (cases.Count != bound.Cases && (cases.Count != 0 || !EndsBeforeTheFirstRun(result.Verdict)))
            return false;

        long printed = 0;
        for (var index = 0; index < cases.Count; index++)
        {
            if (cases[index].CaseIndex != index)
                return false;

            printed += cases[index].ActualStdout.Length;
        }

        // The cases of a suite share the task's stdout cap.
        return printed <= bound.OutputCharacters;
    }

    /// <summary>
    /// Whether a task can end with a verdict before any of its runs: a module that does not
    /// compile, a compile the budget does not pay for, or a fault of the node.
    /// </summary>
    private static bool EndsBeforeTheFirstRun(VerifyVerdict verdict)
    {
        return verdict is VerifyVerdict.CompileError or VerifyVerdict.Timeout or VerifyVerdict.NoResult;
    }

    #endregion

    #region Properties

    /// <summary>The ticket of the task package the bounds are of.</summary>
    public string Ticket { get; }

    #endregion
}
