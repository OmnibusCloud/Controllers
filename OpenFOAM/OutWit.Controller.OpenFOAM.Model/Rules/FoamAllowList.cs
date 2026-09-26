using System.Text.RegularExpressions;

namespace OutWit.Controller.OpenFOAM.Model.Rules;

/// <summary>
/// What a recipe may run. Utilities by name; solvers by shape (a kit
/// executable whose name ends in <c>Foam</c>); and the controller's own
/// steps, which no kit carries. Nothing else: no shell, no script, no utility
/// outside this list, however useful - a case that needs one is refused by
/// name, which is the honest answer before a node would fail on it anyway.
/// Published in the supported-inputs document.
/// </summary>
public static class FoamAllowList
{
    #region Constants

    /// <summary>
    /// The controller's step that puts the initial fields into every
    /// processor directory (<c>restore0Dir -processor</c>, named as OpenFOAM's
    /// <c>RunFunctions</c> name it): what a case needs after it is meshed on
    /// the decomposed case, where <c>decomposePar</c> split the fields over
    /// the background mesh.
    /// </summary>
    public const string RESTORE_INITIAL_FIELDS = "restore0Dir";

    /// <summary>The one form of <see cref="RESTORE_INITIAL_FIELDS"/>: into the processor directories.</summary>
    public const string PROCESSOR_FORM = "-processor";

    /// <summary>The steps the controller does itself: no kit executable, never under MPI.</summary>
    public static readonly IReadOnlyList<string> BUILT_IN_STEPS = [RESTORE_INITIAL_FIELDS];

    /// <summary>
    /// The utilities, in the order they usually run: those of version 1, then
    /// the mesh, set and region utilities and the two initialisations of
    /// version 1.1 (<c>setsToZones</c> ... <c>makeFaMesh</c>).
    /// </summary>
    public static readonly IReadOnlyList<string> UTILITIES =
    [
        "blockMesh",
        "surfaceFeatureExtract",
        "snappyHexMesh",
        "extrudeMesh",
        "refineMesh",
        "setFields",
        "mapFields",
        "topoSet",
        "createPatch",
        "createBaffles",
        "transformPoints",
        "renumberMesh",
        "checkMesh",
        "decomposePar",
        "reconstructPar",
        "reconstructParMesh",
        "postProcess",
        "foamDictionary",
        "potentialFoam",
        "setsToZones",
        "subsetMesh",
        "splitMeshRegions",
        "mergeMeshes",
        "mergeOrSplitBaffles",
        "extrudeToRegionMesh",
        "refineHexMesh",
        "collapseEdges",
        "extrude2DMesh",
        "setAlphaField",
        "makeFaMesh"
    ];

    /// <summary>
    /// The utilities a parallel step may name; a parallel step naming any
    /// other utility is refused. Solvers are all parallel-capable. A utility
    /// is here when the kit accepts <c>-parallel</c> for it and OpenFOAM's
    /// tutorials run it in parallel; one that only accepts the flag runs
    /// serially.
    /// </summary>
    public static readonly IReadOnlyList<string> PARALLEL_CAPABLE_UTILITIES =
    [
        "snappyHexMesh",
        "setFields",
        "topoSet",
        "createPatch",
        "createBaffles",
        "renumberMesh",
        "checkMesh",
        "postProcess",
        "potentialFoam",
        "refineMesh",
        "splitMeshRegions",
        "makeFaMesh"
    ];

    /// <summary>
    /// Flags no step may carry: the controller decides the case directory
    /// and the decomposition, and no step reads anything outside the case.
    /// </summary>
    public static readonly IReadOnlyList<string> FORBIDDEN_FLAGS =
    [
        "-case",
        "-decomposeParDict",
        "-fileHandler",
        "-hostRoots",
        "-roots",
        "-lib",
        "-libs"
    ];

    private static readonly Regex SOLVER_NAME = new("^[A-Za-z][A-Za-z0-9]*Foam$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> UTILITY_SET = new(UTILITIES, StringComparer.Ordinal);

    private static readonly HashSet<string> PARALLEL_SET = new(PARALLEL_CAPABLE_UTILITIES, StringComparer.Ordinal);

    private static readonly HashSet<string> FORBIDDEN_FLAG_SET = new(FORBIDDEN_FLAGS, StringComparer.Ordinal);

    #endregion

    #region Functions

    /// <summary>
    /// Whether the name is an allow-listed utility.
    /// </summary>
    /// <param name="utility">The name.</param>
    /// <returns>True for a utility of the list.</returns>
    public static bool IsUtility(string utility)
    {
        return UTILITY_SET.Contains(utility);
    }

    /// <summary>
    /// Whether the name is one of the controller's own steps.
    /// </summary>
    /// <param name="name">The step's name.</param>
    /// <returns>True for a built-in step.</returns>
    public static bool IsBuiltIn(string name)
    {
        return BUILT_IN_STEPS.Contains(name, StringComparer.Ordinal);
    }

    /// <summary>
    /// Whether the name has the shape of a solver (<c>simpleFoam</c>,
    /// <c>interFoam</c>, <c>rhoPimpleFoam</c>). Whether the kit has it is a
    /// separate question the validator asks the kit.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>True for a solver-shaped name.</returns>
    public static bool IsSolverName(string name)
    {
        return SOLVER_NAME.IsMatch(name) && !UTILITY_SET.Contains(name);
    }

    /// <summary>
    /// Whether a step naming this executable may run under MPI.
    /// </summary>
    /// <param name="name">The executable's name.</param>
    /// <returns>True for solvers and the parallel-capable utilities.</returns>
    public static bool IsParallelCapable(string name)
    {
        return IsSolverName(name) || PARALLEL_SET.Contains(name);
    }

    /// <summary>
    /// Whether the flag is one no step may carry.
    /// </summary>
    /// <param name="flag">An argument token starting with '-'.</param>
    /// <returns>True when forbidden.</returns>
    public static bool IsForbiddenFlag(string flag)
    {
        return FORBIDDEN_FLAG_SET.Contains(flag);
    }

    #endregion
}
