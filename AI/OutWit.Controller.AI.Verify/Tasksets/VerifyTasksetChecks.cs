using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// What Split refuses a taskset for and the preflight says beforehand, beyond what the parser
/// finds line by line.
/// </summary>
public static class VerifyTasksetChecks
{
    #region Constants

    /// <summary>What the preflight says of a taskset or options that set a wall time.</summary>
    public const string WALL_TIME_NOTE =
        "limits.wall_ms is not a limit: a run is bounded by its fuel, and Timeout means out of fuel (the wall clock is a node's own backstop and gives no verdict).";

    private const int MAX_SHOWN_ID_LENGTH = 64;

    #endregion

    #region Functions

    /// <summary>The runtime ids of a taskset that this controller does not have, each once, in first-seen order.</summary>
    /// <param name="tasks">The parsed tasks.</param>
    /// <returns>The ids, as a message may show them.</returns>
    public static List<string> UnknownRuntimes(IEnumerable<VerifyTaskData> tasks)
    {
        return tasks
            .Select(task => task.RuntimeId)
            .Distinct(StringComparer.Ordinal)
            .Where(id => VerifyRuntimeCatalog.Find(id) == null)
            .Select(Shown)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Whether a taskset or its options set a wall time, which is no limit any more.</summary>
    /// <param name="tasks">The parsed tasks.</param>
    /// <param name="options">The run's options, or null.</param>
    /// <returns>True when one does.</returns>
    public static bool SetsWallTime(IEnumerable<VerifyTaskData> tasks, VerifyOptionsData? options)
    {
        return SetsWallTime(options) || tasks.Any(task => task.Limits is { WallTimeMs: > 0 });
    }

    /// <summary>Whether a run's options set a wall time, which is no limit any more.</summary>
    /// <param name="options">The run's options, or null.</param>
    /// <returns>True when they do.</returns>
    public static bool SetsWallTime(VerifyOptionsData? options)
    {
        return options?.DefaultLimits is { WallTimeMs: > 0 };
    }

    /// <summary>A runtime id as a message may show it: bounded, and only the characters an id has.</summary>
    /// <param name="id">The id as a task names it.</param>
    /// <returns>The id cut to a length, with anything but letters, digits, a dot, a dash and an underscore as '?'.</returns>
    public static string Shown(string id)
    {
        var shown = id.Length > MAX_SHOWN_ID_LENGTH ? id[..MAX_SHOWN_ID_LENGTH] : id;
        return string.Concat(shown.Select(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '?'));
    }

    #endregion
}
