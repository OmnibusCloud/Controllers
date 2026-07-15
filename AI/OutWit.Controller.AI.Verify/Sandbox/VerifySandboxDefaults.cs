using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// Sandbox-level limit defaults and the task → batch → default resolution chain
/// (a limit field value of 0 means "unset").
/// </summary>
public static class VerifySandboxDefaults
{
    #region Constants

    /// <summary>
    /// CPU budget in fuel units. Python-scale anchor: hello-world ≈ 1.6e8, fib(27) ≈ 8.4e8 —
    /// 5e10 buys tens of interpreter-seconds; explicit tasksets set their own budgets.
    /// </summary>
    public const long DEFAULT_FUEL_BUDGET = 50_000_000_000;

    public const long DEFAULT_MEMORY_BYTES = 256 * 1024 * 1024;

    public const int DEFAULT_WALL_TIME_MS = 10_000;

    public const int DEFAULT_STDOUT_LIMIT_BYTES = 256 * 1024;

    public const int DEFAULT_STDERR_LIMIT_BYTES = 64 * 1024;

    #endregion

    #region Functions

    /// <summary>
    /// Effective limits for one task: per-field fallback task → batch default → sandbox default.
    /// </summary>
    public static VerifyLimitsData Resolve(VerifyLimitsData? task, VerifyLimitsData? batch)
    {
        return new VerifyLimitsData
        {
            FuelBudget = Pick(task?.FuelBudget, batch?.FuelBudget, DEFAULT_FUEL_BUDGET),
            MemoryBytes = Pick(task?.MemoryBytes, batch?.MemoryBytes, DEFAULT_MEMORY_BYTES),
            WallTimeMs = (int)Pick(task?.WallTimeMs, batch?.WallTimeMs, DEFAULT_WALL_TIME_MS),
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
