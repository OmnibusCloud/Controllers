using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// Upper bounds on per-task limits. A task may request less; it can never get more. They are
/// applied twice: where a taskset is split (with a note for the preflight), and again on the node
/// before a task runs, whatever the package that brought it says.
/// </summary>
public static class VerifyLimitCeilings
{
    #region Constants

    /// <summary>
    /// Ceiling on a run's fuel budget: two minutes of a busy loop on the slowest node
    /// (<see cref="VerifySandboxDefaults.REFERENCE_FUEL_PER_SECOND"/>), the longest a run could
    /// take while a wall limit of two minutes ended it. Fuel is the only budget of a run's work
    /// now, so its ceiling is what bounds a run's time.
    /// </summary>
    public const long MAX_FUEL_BUDGET = 120 * VerifySandboxDefaults.REFERENCE_FUEL_PER_SECOND;

    /// <summary>Ceiling on a task's linear memory.</summary>
    public const long MAX_MEMORY_BYTES = 2L * 1024 * 1024 * 1024;

    /// <summary>Ceiling on the stdout a task may keep.</summary>
    public const int MAX_STDOUT_LIMIT_BYTES = 8 * 1024 * 1024;

    /// <summary>Ceiling on the stderr a task may keep.</summary>
    public const int MAX_STDERR_LIMIT_BYTES = 1024 * 1024;

    #endregion

    #region Functions

    /// <summary>
    /// Clamps a resolved limit set to the ceilings. Returns the clamped limits and,
    /// per clamped field, a human-readable note for preflight.
    /// </summary>
    /// <param name="limits">The resolved limits.</param>
    /// <param name="taskIndex">The task, for the notes.</param>
    /// <returns>The clamped limits and one note per field that was clamped.</returns>
    public static (VerifyLimitsData Clamped, List<string> Notes) Clamp(VerifyLimitsData limits, int taskIndex)
    {
        var notes = new List<string>();
        var clamped = new VerifyLimitsData
        {
            FuelBudget = ClampField(limits.FuelBudget, MAX_FUEL_BUDGET, "fuel", taskIndex, notes),
            MemoryBytes = ClampField(limits.MemoryBytes, MAX_MEMORY_BYTES, "memory", taskIndex, notes),
            WallTimeMs = limits.WallTimeMs,
            StdoutLimitBytes = (int)ClampField(limits.StdoutLimitBytes, MAX_STDOUT_LIMIT_BYTES, "stdout_bytes", taskIndex, notes),
            StderrLimitBytes = (int)ClampField(limits.StderrLimitBytes, MAX_STDERR_LIMIT_BYTES, "stderr_bytes", taskIndex, notes)
        };
        return (clamped, notes);
    }

    /// <summary>
    /// The limits a node runs a task under: resolved, every field set, and within the ceilings.
    /// </summary>
    /// <param name="task">The task's own limits, or null.</param>
    /// <param name="batch">The batch's defaults, or null.</param>
    /// <returns>The limits.</returns>
    public static VerifyLimitsData Effective(VerifyLimitsData? task, VerifyLimitsData? batch)
    {
        return Clamp(VerifySandboxDefaults.Resolve(task, batch), 0).Clamped;
    }

    private static long ClampField(long value, long ceiling, string name, int taskIndex, List<string> notes)
    {
        if (value <= ceiling)
            return value;

        notes.Add($"task {taskIndex}: {name} {value} clamped to ceiling {ceiling}");
        return ceiling;
    }

    #endregion
}
