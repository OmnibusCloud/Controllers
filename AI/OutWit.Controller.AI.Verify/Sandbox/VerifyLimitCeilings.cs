using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// Host-enforced upper bounds on per-task limits, applied at split/preflight time so a
/// taskset cannot ask a node for more than the network permits. A task may request less;
/// it can never request more.
/// </summary>
public static class VerifyLimitCeilings
{
    public const long MAX_FUEL_BUDGET = 2_000_000_000_000; // ~thousands of interpreter-seconds
    public const long MAX_MEMORY_BYTES = 2L * 1024 * 1024 * 1024;
    public const int MAX_WALL_TIME_MS = 120_000;
    public const int MAX_STDOUT_LIMIT_BYTES = 8 * 1024 * 1024;
    public const int MAX_STDERR_LIMIT_BYTES = 1024 * 1024;

    /// <summary>
    /// Clamps a resolved limit set to the ceilings. Returns the clamped limits and,
    /// per clamped field, a human-readable note for preflight.
    /// </summary>
    public static (VerifyLimitsData Clamped, List<string> Notes) Clamp(VerifyLimitsData limits, int taskIndex)
    {
        var notes = new List<string>();
        var clamped = new VerifyLimitsData
        {
            FuelBudget = ClampField(limits.FuelBudget, MAX_FUEL_BUDGET, "fuel", taskIndex, notes),
            MemoryBytes = ClampField(limits.MemoryBytes, MAX_MEMORY_BYTES, "memory", taskIndex, notes),
            WallTimeMs = (int)ClampField(limits.WallTimeMs, MAX_WALL_TIME_MS, "wall_ms", taskIndex, notes),
            StdoutLimitBytes = (int)ClampField(limits.StdoutLimitBytes, MAX_STDOUT_LIMIT_BYTES, "stdout_bytes", taskIndex, notes),
            StderrLimitBytes = (int)ClampField(limits.StderrLimitBytes, MAX_STDERR_LIMIT_BYTES, "stderr_bytes", taskIndex, notes)
        };
        return (clamped, notes);
    }

    private static long ClampField(long value, long ceiling, string name, int taskIndex, List<string> notes)
    {
        if (value <= ceiling)
            return value;

        notes.Add($"task {taskIndex}: {name} {value} clamped to ceiling {ceiling}");
        return ceiling;
    }
}
