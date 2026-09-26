namespace OutWit.Controller.OpenFOAM.Model.Rules;

/// <summary>
/// The solver classes a case is estimated by (<see cref="FoamCaseData.SolverClass"/>):
/// the vocabulary the node's work estimate prices and the initiator names,
/// and which solver of the pinned build falls into which class. A solver not
/// in the table has no class - the estimate then counts it as steady
/// incompressible, which is the honest "unknown", not a guess dressed as one.
/// Published in the supported-inputs document.
/// </summary>
public static class FoamSolverClasses
{
    #region Constants

    public const string INCOMPRESSIBLE_STEADY = "incompressible-steady";

    public const string INCOMPRESSIBLE_TRANSIENT = "incompressible-transient";

    public const string COMPRESSIBLE_STEADY = "compressible-steady";

    public const string COMPRESSIBLE_TRANSIENT = "compressible-transient";

    public const string THERMAL_STEADY = "thermal-steady";

    public const string THERMAL_TRANSIENT = "thermal-transient";

    public const string MULTIPHASE_TRANSIENT = "multiphase-transient";

    /// <summary>Every class, in the order the supported-inputs document lists them.</summary>
    public static readonly IReadOnlyList<string> ALL =
    [
        INCOMPRESSIBLE_STEADY,
        INCOMPRESSIBLE_TRANSIENT,
        COMPRESSIBLE_STEADY,
        COMPRESSIBLE_TRANSIENT,
        THERMAL_STEADY,
        THERMAL_TRANSIENT,
        MULTIPHASE_TRANSIENT
    ];

    /// <summary>
    /// The solvers of OpenFOAM v2606 (<c>applications/solvers</c>) with a
    /// class, by application name. Combustion, Lagrangian, finite-area,
    /// electromagnetic and the other specialised solvers have none;
    /// <c>potentialFoam</c> is an allow-listed utility (a recipe's
    /// initialisation step), never a recipe's application.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> APPLICATIONS = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["simpleFoam"] = INCOMPRESSIBLE_STEADY,
        ["porousSimpleFoam"] = INCOMPRESSIBLE_STEADY,
        ["SRFSimpleFoam"] = INCOMPRESSIBLE_STEADY,
        ["overSimpleFoam"] = INCOMPRESSIBLE_STEADY,
        ["boundaryFoam"] = INCOMPRESSIBLE_STEADY,
        ["adjointOptimisationFoam"] = INCOMPRESSIBLE_STEADY,
        ["adjointShapeOptimizationFoam"] = INCOMPRESSIBLE_STEADY,

        ["pimpleFoam"] = INCOMPRESSIBLE_TRANSIENT,
        ["pisoFoam"] = INCOMPRESSIBLE_TRANSIENT,
        ["icoFoam"] = INCOMPRESSIBLE_TRANSIENT,
        ["nonNewtonianIcoFoam"] = INCOMPRESSIBLE_TRANSIENT,
        ["SRFPimpleFoam"] = INCOMPRESSIBLE_TRANSIENT,
        ["overPimpleDyMFoam"] = INCOMPRESSIBLE_TRANSIENT,
        ["shallowWaterFoam"] = INCOMPRESSIBLE_TRANSIENT,

        ["rhoSimpleFoam"] = COMPRESSIBLE_STEADY,
        ["rhoPorousSimpleFoam"] = COMPRESSIBLE_STEADY,
        ["overRhoSimpleFoam"] = COMPRESSIBLE_STEADY,

        ["rhoPimpleFoam"] = COMPRESSIBLE_TRANSIENT,
        ["rhoCentralFoam"] = COMPRESSIBLE_TRANSIENT,
        ["rhoPimpleAdiabaticFoam"] = COMPRESSIBLE_TRANSIENT,
        ["overRhoPimpleDyMFoam"] = COMPRESSIBLE_TRANSIENT,
        ["sonicFoam"] = COMPRESSIBLE_TRANSIENT,
        ["sonicDyMFoam"] = COMPRESSIBLE_TRANSIENT,
        ["sonicLiquidFoam"] = COMPRESSIBLE_TRANSIENT,

        ["buoyantSimpleFoam"] = THERMAL_STEADY,
        ["buoyantBoussinesqSimpleFoam"] = THERMAL_STEADY,
        ["chtMultiRegionSimpleFoam"] = THERMAL_STEADY,

        ["buoyantPimpleFoam"] = THERMAL_TRANSIENT,
        ["buoyantBoussinesqPimpleFoam"] = THERMAL_TRANSIENT,
        ["overBuoyantPimpleDyMFoam"] = THERMAL_TRANSIENT,
        ["chtMultiRegionFoam"] = THERMAL_TRANSIENT,
        ["laplacianFoam"] = THERMAL_TRANSIENT,
        ["solidFoam"] = THERMAL_TRANSIENT,

        ["interFoam"] = MULTIPHASE_TRANSIENT,
        ["interIsoFoam"] = MULTIPHASE_TRANSIENT,
        ["interMixingFoam"] = MULTIPHASE_TRANSIENT,
        ["overInterDyMFoam"] = MULTIPHASE_TRANSIENT,
        ["interPhaseChangeFoam"] = MULTIPHASE_TRANSIENT,
        ["interPhaseChangeDyMFoam"] = MULTIPHASE_TRANSIENT,
        ["compressibleInterFoam"] = MULTIPHASE_TRANSIENT,
        ["compressibleInterIsoFoam"] = MULTIPHASE_TRANSIENT,
        ["compressibleInterDyMFoam"] = MULTIPHASE_TRANSIENT,
        ["multiphaseInterFoam"] = MULTIPHASE_TRANSIENT,
        ["multiphaseEulerFoam"] = MULTIPHASE_TRANSIENT,
        ["twoPhaseEulerFoam"] = MULTIPHASE_TRANSIENT,
        ["reactingTwoPhaseEulerFoam"] = MULTIPHASE_TRANSIENT,
        ["reactingMultiphaseEulerFoam"] = MULTIPHASE_TRANSIENT,
        ["driftFluxFoam"] = MULTIPHASE_TRANSIENT,
        ["cavitatingFoam"] = MULTIPHASE_TRANSIENT,
        ["twoLiquidMixingFoam"] = MULTIPHASE_TRANSIENT,
        ["potentialFreeSurfaceFoam"] = MULTIPHASE_TRANSIENT
    };

    private static readonly HashSet<string> CLASS_SET = new(ALL, StringComparer.OrdinalIgnoreCase);

    #endregion

    #region Functions

    /// <summary>
    /// The class of a solver.
    /// </summary>
    /// <param name="application">The application as <c>controlDict</c> names it (case-sensitive, as on the node).</param>
    /// <returns>The class, or an empty string when the solver has none.</returns>
    public static string ClassOf(string application)
    {
        return APPLICATIONS.TryGetValue(application, out var solverClass) ? solverClass : string.Empty;
    }

    /// <summary>
    /// Whether a class belongs to the vocabulary; case is ignored, as the work
    /// estimate has always matched it.
    /// </summary>
    /// <param name="solverClass">The class.</param>
    /// <returns>True for a class of <see cref="ALL"/>.</returns>
    public static bool IsKnown(string solverClass)
    {
        return CLASS_SET.Contains(solverClass);
    }

    #endregion
}
