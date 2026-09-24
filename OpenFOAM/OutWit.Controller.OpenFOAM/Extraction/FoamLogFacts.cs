namespace OutWit.Controller.OpenFOAM.Extraction;

/// <summary>
/// What a solver log says about the run: how far it got, whether it
/// converged, the last residuals, how many warnings it printed, and whether
/// it died of a floating-point exception (a divergence under FOAM_SIGFPE).
/// </summary>
public sealed class FoamLogFacts
{
    #region Properties

    /// <summary>Time steps or iterations taken (the count of <c>Time =</c> lines).</summary>
    public int Iterations { get; set; }

    /// <summary>The last time value printed; 0 when none.</summary>
    public double FinalTime { get; set; }

    /// <summary>True when the solver reported convergence (steady solvers) or a controlled stop.</summary>
    public bool Converged { get; set; }

    /// <summary>True when the log ends with OpenFOAM's <c>End</c>.</summary>
    public bool ReachedEnd { get; set; }

    /// <summary>True when a FOAM FATAL ERROR or IO ERROR was printed.</summary>
    public bool Fatal { get; set; }

    /// <summary>True when the run died of a floating-point exception or a sigFpe.</summary>
    public bool FloatingPointException { get; set; }

    /// <summary>Number of FOAM warnings.</summary>
    public int WarningCount { get; set; }

    /// <summary>Cell count printed by a mesh report (<c>nCells:</c> or <c>cells:</c>); 0 when absent.</summary>
    public long CellCount { get; set; }

    /// <summary>The initial residual of every solved field at the last iteration, in first-seen field order.</summary>
    public List<KeyValuePair<string, double>> FinalResiduals { get; } = [];

    #endregion
}
