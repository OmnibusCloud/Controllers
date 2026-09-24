using OutWit.Controller.OpenFOAM.Model;

namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// What <see cref="FoamCaseRunner"/> hands back: every step's outcome in
/// order and, when a step failed, which one, with what code and what it
/// said last.
/// </summary>
public sealed class FoamRunReport
{
    #region Properties

    /// <summary>The steps that ran (and the decomposition steps skipped on a node without MPI, at zero ranks).</summary>
    public List<FoamStepOutcomeData> Steps { get; } = [];

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
