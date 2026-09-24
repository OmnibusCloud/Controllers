using System.Text.RegularExpressions;

namespace OutWit.Controller.OpenFOAM.Recipes;

/// <summary>
/// What a recipe may run. Utilities by name; solvers by shape (a kit
/// executable whose name ends in <c>Foam</c>). Nothing else: no shell, no
/// script, no utility outside this list, however useful - a case that needs
/// one is refused by name, which is the honest answer before a node would
/// fail on it anyway. Published in the supported-inputs document.
/// </summary>
public static class FoamAllowList
{
    #region Constants

    /// <summary>The utilities of version 1, in the order they usually run.</summary>
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
        "potentialFoam"
    ];

    /// <summary>
    /// The utilities that accept <c>-parallel</c>; a parallel step naming any
    /// other utility is refused. Solvers are all parallel-capable.
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
        "refineMesh"
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
