using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// Sandbox-level limit defaults and the task → batch → default resolution chain
/// (a limit field value of 0 means "unset"). Fuel is the one budget of a run's work: the wall
/// clock is a node's own backstop (<see cref="VerifyBackstop"/>) and no limit a task sets.
/// </summary>
public static class VerifySandboxDefaults
{
    #region Constants

    /// <summary>
    /// The fuel a plain busy loop burns a second on the slowest node the fleet has shown (an
    /// interpreter's loop burns 2 to 3.4 thousand million there): what a fuel budget is turned
    /// into seconds by, in the documents and in the sizing of the backstop.
    /// </summary>
    public const long REFERENCE_FUEL_PER_SECOND = 2_000_000_000;

    /// <summary>
    /// CPU budget in fuel units when the task and the batch set none: ten seconds of a busy loop
    /// on the slowest node (<see cref="REFERENCE_FUEL_PER_SECOND"/>), which is what the default
    /// wall limit of ten seconds allowed there before fuel alone ended a run. Python-scale
    /// anchor: a hello ≈ 1.2e8, fib(27) ≈ 8.4e8.
    /// </summary>
    public const long DEFAULT_FUEL_BUDGET = 10 * REFERENCE_FUEL_PER_SECOND;

    /// <summary>Linear memory cap when the task and the batch set none.</summary>
    public const long DEFAULT_MEMORY_BYTES = 256 * 1024 * 1024;

    /// <summary>Captured stdout cap of a task when it and the batch set none; the write that passes it ends the run as OutputExceeded.</summary>
    public const int DEFAULT_STDOUT_LIMIT_BYTES = 256 * 1024;

    /// <summary>Captured stderr cap when the task and the batch set none.</summary>
    public const int DEFAULT_STDERR_LIMIT_BYTES = 64 * 1024;

    #endregion

    #region Functions

    /// <summary>
    /// Effective limits for one task: per-field fallback task → batch default → sandbox default.
    /// The wall time is carried as given and used by nothing: it is not a limit.
    /// </summary>
    /// <param name="task">The task's own limits, or null.</param>
    /// <param name="batch">The batch's defaults, or null.</param>
    /// <returns>The limits with every field set.</returns>
    public static VerifyLimitsData Resolve(VerifyLimitsData? task, VerifyLimitsData? batch)
    {
        return new VerifyLimitsData
        {
            FuelBudget = Pick(task?.FuelBudget, batch?.FuelBudget, DEFAULT_FUEL_BUDGET),
            MemoryBytes = Pick(task?.MemoryBytes, batch?.MemoryBytes, DEFAULT_MEMORY_BYTES),
            WallTimeMs = (int)Pick(task?.WallTimeMs, batch?.WallTimeMs, 0),
            StdoutLimitBytes = (int)Pick(task?.StdoutLimitBytes, batch?.StdoutLimitBytes, DEFAULT_STDOUT_LIMIT_BYTES),
            StderrLimitBytes = (int)Pick(task?.StderrLimitBytes, batch?.StderrLimitBytes, DEFAULT_STDERR_LIMIT_BYTES)
        };
    }

    private static long Pick(long? task, long? batch, long fallback)
    {
        if (task is > 0)
            return task.Value;

        if (batch is > 0)
            return batch.Value;

        return fallback;
    }

    private static long Pick(int? task, int? batch, int fallback)
    {
        return Pick(task == null ? null : (long)task.Value, batch == null ? null : (long)batch.Value, fallback);
    }

    #endregion
}
