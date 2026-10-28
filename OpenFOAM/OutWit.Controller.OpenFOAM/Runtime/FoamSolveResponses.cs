using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Model.Rules;

namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// Which responses the controller adds to the solve on its own: the
/// statistics, min/max and probes (<c>PatchValue</c>, <c>VolumeValue</c>,
/// <c>FieldMinMax</c>, <c>Probe</c>) a recipe measures after the solve with
/// a post step. A post step (<c>postProcess -func &lt;response&gt;
/// -latestTime</c>) reads the latest written time, which is not the run's
/// final time when <c>endTime</c> is not a multiple of <c>writeInterval</c> -
/// sweeping either does exactly that - and the response came back blank,
/// left out as stale (measured on v2606, 2026-10-28). Measured by the solver
/// at every step as well, its last row is the final time's, and the
/// extractor reads the row at the final time whichever step wrote it. A
/// response the recipe already adds with its own <c>includeFunc</c> step is
/// left to that step; one that is a function object the case runs itself
/// (by name, wherever the case writes it - <see cref="FoamCaseFunctionObjects"/>)
/// is the case's to measure and is not added, so the node's copy of the
/// case gains no line that would change what the case does. Forces stay
/// as the recipe says: the recipe's <c>includeFunc</c> step is theirs.
/// </summary>
public static class FoamSolveResponses
{
    #region Constants

    private const string CONTROL_DICT = "system/controlDict";

    private const string POST_PROCESS_FLAG = "-postProcess";

    /// <summary>The kinds a recipe measures after the solve, which the controller measures during it as well.</summary>
    public static readonly IReadOnlyList<FoamResponseKind> MEASURED_IN_SOLVE =
    [
        FoamResponseKind.PatchValue, FoamResponseKind.VolumeValue, FoamResponseKind.FieldMinMax, FoamResponseKind.Probe
    ];

    #endregion

    #region Functions

    /// <summary>
    /// The responses the controller adds to the solve, by name, in the
    /// request's order.
    /// </summary>
    /// <param name="caseDirectory">The materialised case, its controlDict as the variant has it.</param>
    /// <param name="recipe">The recipe; without a step that solves, nothing is added.</param>
    /// <param name="extraction">The request; null adds nothing.</param>
    /// <returns>The names; empty when there is nothing to add.</returns>
    public static IReadOnlyList<string> MeasuredInSolve(string caseDirectory, FoamRecipeData recipe, FoamExtractionRequestData? extraction)
    {
        if (extraction == null || extraction.Responses.Count == 0 || !recipe.Steps.Any(step => IsSolve(step, recipe.Application)))
            return [];

        var byRecipe = recipe.Steps
            .Where(step => step.Utility == FoamAllowList.INCLUDE_FUNCTION && step.Arguments.Count == 1)
            .Select(step => step.Arguments[0])
            .ToHashSet(StringComparer.Ordinal);

        var byCase = CaseFunctionObjects(caseDirectory);

        return extraction.Responses
            .Where(response => MEASURED_IN_SOLVE.Contains(response.Kind))
            .Select(response => response.Name)
            .Where(name => !byRecipe.Contains(name) && !byCase.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The names of the function objects the materialised case runs itself,
    /// read from its controlDict as the variant has it.
    /// </summary>
    /// <param name="caseDirectory">The materialised case.</param>
    /// <returns>The names; empty when the case has no controlDict or no functions.</returns>
    public static IReadOnlySet<string> CaseFunctionObjects(string caseDirectory)
    {
        var root = Path.GetFullPath(caseDirectory);
        var controlDict = ReadCaseFile(root, CONTROL_DICT);
        if (controlDict == null)
            return new HashSet<string>(StringComparer.Ordinal);

        return FoamCaseFunctionObjects.Enumerate(controlDict, relativePath => ReadCaseFile(root, relativePath))
            .Select(function => function.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    #endregion

    #region Tools

    private static string? ReadCaseFile(string root, string relativePath)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!Path.GetFullPath(path).StartsWith(root, StringComparison.Ordinal) || !File.Exists(path))
            return null;

        try
        {
            return FoamCaseText.FromBytes(File.ReadAllBytes(path));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsSolve(FoamStepData step, string application)
    {
        return step.Utility == application && !step.Arguments.Contains(POST_PROCESS_FLAG);
    }

    #endregion
}
