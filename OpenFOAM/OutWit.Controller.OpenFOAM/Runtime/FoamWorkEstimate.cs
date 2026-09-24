using OutWit.Controller.OpenFOAM.Model;

namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// The relative cost of a variant for the scheduler, from the task's scalars
/// alone (never from the case tree, which the scheduler must not open): cells against a
/// reference mesh, a factor for the solver class, a surcharge when every
/// variant meshes again. Initial values, to be recalibrated against the
/// oracle cases; an unknown class counts as steady incompressible.
/// </summary>
public static class FoamWorkEstimate
{
    #region Constants

    /// <summary>Cell count the estimate is normalized to (a variant of this size and class costs 1.0).</summary>
    public const double REFERENCE_CELLS = 100_000;

    /// <summary>Surcharge on a variant that meshes as well as solves.</summary>
    public const double MESHING_FACTOR = 1.5;

    /// <summary>The estimate for a task without a cell count: one unit, like any unknown.</summary>
    public const double UNKNOWN = 1.0;

    /// <summary>Cost relative to a steady incompressible run of the same mesh, by solver class.</summary>
    public static readonly IReadOnlyDictionary<string, double> SOLVER_CLASS_FACTORS = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
    {
        ["incompressible-steady"] = 1.0,
        ["incompressible-transient"] = 6.0,
        ["compressible-steady"] = 2.0,
        ["compressible-transient"] = 10.0,
        ["multiphase-transient"] = 12.0,
        ["thermal-steady"] = 1.5,
        ["thermal-transient"] = 8.0
    };

    #endregion

    #region Functions

    /// <summary>
    /// Estimates a task's work.
    /// </summary>
    /// <param name="task">The task; its CellCount, SolverClass and Recipe.MeshesPerVariant are read.</param>
    /// <returns>A positive relative cost; <see cref="UNKNOWN"/> when the cell count is missing.</returns>
    public static double Estimate(FoamTaskData task)
    {
        if (task.CellCount <= 0)
            return UNKNOWN;

        var factor = SOLVER_CLASS_FACTORS.TryGetValue(task.SolverClass, out var known) ? known : 1.0;
        var meshing = task.Recipe?.MeshesPerVariant == true ? MESHING_FACTOR : 1.0;

        return task.CellCount / REFERENCE_CELLS * factor * meshing;
    }

    #endregion
}
