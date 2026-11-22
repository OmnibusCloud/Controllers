namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// The wall-clock backstop of one run. The clock gives no verdict: <c>Timeout</c> means out of
/// fuel, which is the same on every node, and a run the clock ended is a fault of the node that
/// ran it (the task is sent out once more, and reported as <c>NoResult</c> if no node does
/// better). The backstop only keeps a worker from being held without end, by a run whose
/// instructions are far slower than a busy loop's or by a node that is starved of processor
/// time. It is sized from the run's fuel budget: the time the budget lasts at a quarter of the
/// reference rate, and ten seconds for the start of a run.
/// </summary>
public static class VerifyBackstop
{
    #region Constants

    /// <summary>
    /// The slowest a run may burn its fuel before the clock ends it: a quarter of the reference
    /// rate (<see cref="VerifySandboxDefaults.REFERENCE_FUEL_PER_SECOND"/>).
    /// </summary>
    public const long SLOWEST_FUEL_PER_SECOND = VerifySandboxDefaults.REFERENCE_FUEL_PER_SECOND / 4;

    /// <summary>What every run gets besides: the start of a store and of an interpreter, milliseconds.</summary>
    public const int BASE_MS = 10_000;

    #endregion

    #region Functions

    /// <summary>
    /// The backstop of a run with a fuel budget: 50 seconds for the default budget, 8 minutes and
    /// 10 seconds for the largest.
    /// </summary>
    /// <param name="fuelBudget">The run's fuel budget.</param>
    /// <returns>The wall time after which the node ends the run as its own fault, milliseconds.</returns>
    public static int WallMs(long fuelBudget)
    {
        var fuelMs = Math.Max(0, fuelBudget) / (SLOWEST_FUEL_PER_SECOND / 1000);
        return (int)Math.Min(int.MaxValue, BASE_MS + fuelMs);
    }

    #endregion
}
