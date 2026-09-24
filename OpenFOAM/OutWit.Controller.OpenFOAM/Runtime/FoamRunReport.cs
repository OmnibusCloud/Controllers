using OutWit.Controller.OpenFOAM.Model;

namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// What <see cref="FoamCaseRunner"/> hands back: every step's outcome in
/// order with the log it wrote, and, when a step failed, which one, with what
/// code and what it said last.
/// </summary>
public sealed class FoamRunReport
{
    #region Fields

    private readonly List<(FoamStepData Step, string LogPath)> m_logs = [];

    #endregion

    #region Functions

    /// <summary>
    /// Records a step that ran and the log it wrote.
    /// </summary>
    /// <param name="step">The step as the recipe stated it.</param>
    /// <param name="outcome">How it ended.</param>
    /// <param name="logPath">The log file it wrote.</param>
    public void Add(FoamStepData step, FoamStepOutcomeData outcome, string logPath)
    {
        Steps.Add(outcome);
        m_logs.Add((step, logPath));
    }

    /// <summary>
    /// The log of the first step that ran and matches.
    /// </summary>
    /// <param name="predicate">Which step.</param>
    /// <returns>Its log path, or null when no such step ran.</returns>
    public string? LogPathOf(Func<FoamStepData, bool> predicate)
    {
        foreach (var (step, logPath) in m_logs)
        {
            if (predicate(step))
                return logPath;
        }

        return null;
    }

    #endregion

    #region Properties

    /// <summary>The steps' outcomes in order (a decomposition step skipped on a node without MPI appears at zero ranks and has no log).</summary>
    public List<FoamStepOutcomeData> Steps { get; } = [];

    /// <summary>The logs the steps that ran wrote, in order.</summary>
    public IReadOnlyList<string> LogPaths => m_logs.Select(entry => entry.LogPath).ToList();

    /// <summary>The utility of the failed step; null when every step succeeded.</summary>
    public string? FailedStep { get; set; }

    /// <summary>The failed step's exit code; 0 when none failed.</summary>
    public int ExitCode { get; set; }

    /// <summary>The failed step's log tail; null when none failed.</summary>
    public string? LogTail { get; set; }

    /// <summary>True when every step succeeded.</summary>
    public bool Succeeded => FailedStep == null;

    #endregion
}
