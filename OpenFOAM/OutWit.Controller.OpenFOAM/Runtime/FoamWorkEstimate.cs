using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Model.Rules;

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

    /// <summary>Cost relative to a steady incompressible run of the same mesh, by solver class (the Model's vocabulary).</summary>
    public static readonly IReadOnlyDictionary<string, double> SOLVER_CLASS_FACTORS = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
    {
        [FoamSolverClasses.INCOMPRESSIBLE_STEADY] = 1.0,
        [FoamSolverClasses.INCOMPRESSIBLE_TRANSIENT] = 6.0,
        [FoamSolverClasses.COMPRESSIBLE_STEADY] = 2.0,
        [FoamSolverClasses.COMPRESSIBLE_TRANSIENT] = 10.0,
        [FoamSolverClasses.MULTIPHASE_TRANSIENT] = 12.0,
        [FoamSolverClasses.THERMAL_STEADY] = 1.5,
        [FoamSolverClasses.THERMAL_TRANSIENT] = 8.0
    };

    #endregion

    #region Functions

    /// <summary>
    /// Estimates one run of a case.
    /// </summary>
    /// <param name="data">The case; its CellCount, SolverClass and Recipe.MeshesPerVariant are read. Null is unknown.</param>
    /// <returns>A positive relative cost; <see cref="UNKNOWN"/> when the case or its cell count is missing.</returns>
    public static double Estimate(FoamCaseData? data)
    {
        if (data == null || data.CellCount <= 0)
            return UNKNOWN;

        var factor = SOLVER_CLASS_FACTORS.TryGetValue(data.SolverClass, out var known) ? known : 1.0;
        var meshing = data.Recipe?.MeshesPerVariant == true ? MESHING_FACTOR : 1.0;

        return data.CellCount / REFERENCE_CELLS * factor * meshing;
    }

    #endregion
}
