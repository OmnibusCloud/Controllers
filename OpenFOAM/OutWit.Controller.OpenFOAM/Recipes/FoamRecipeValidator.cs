using System.Text.RegularExpressions;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Runtime;

namespace OutWit.Controller.OpenFOAM.Recipes;

/// <summary>
/// Validates a recipe before the first process starts: every step against
/// the allow-list and the argument grammar, the application against the kit.
/// The wording is the same the initiator's preflight uses, so a user sees the
/// same sentence on the client and in a refused variant's result.
/// </summary>
public static class FoamRecipeValidator
{
    #region Constants

    /// <summary>A flag: a dash, a letter, then letters, digits or dashes.</summary>
    private static readonly Regex FLAG = new("^-[A-Za-z][A-Za-z0-9-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// A value: OpenFOAM words, numbers, relative paths, lists in parentheses
    /// and comma lists - no shell metacharacters, no quotes, no whitespace.
    /// </summary>
    private static readonly Regex VALUE = new(@"^[A-Za-z0-9_][A-Za-z0-9_.,:=+()/\-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WORD = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A number, which may start with a minus and is a value, not a flag (<c>-time -1</c>).</summary>
    private static readonly Regex NUMBER = new(@"^-?\d+(\.\d+)?([eE][-+]?\d+)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Upper bound on the steps of one recipe; a longer one is a script, not a recipe.</summary>
    public const int MAX_STEPS = 32;

    #endregion

    #region Functions

    /// <summary>
    /// Validates a recipe.
    /// </summary>
    /// <param name="recipe">The recipe; null is a finding.</param>
    /// <param name="kit">The kit the steps will run under, when known; its executables are checked.</param>
    /// <returns>Findings, one sentence each; empty when the recipe may run.</returns>
    public static IReadOnlyList<string> Validate(FoamRecipeData? recipe, FoamKit? kit = null)
    {
        var findings = new List<string>();

        if (recipe == null)
        {
            findings.Add("The task carries no recipe.");
            return findings;
        }

        if (string.IsNullOrEmpty(recipe.Application))
            findings.Add("The recipe names no application.");
        else if (!FoamAllowList.IsSolverName(recipe.Application))
            findings.Add($"'{recipe.Application}' is not a solver name (a solver's name ends in 'Foam').");
        else if (kit != null && !kit.HasExecutable(recipe.Application))
            findings.Add($"The kit has no solver '{recipe.Application}'.");

        if (recipe.Steps.Count == 0)
            findings.Add("The recipe has no steps.");
        if (recipe.Steps.Count > MAX_STEPS)
            findings.Add($"The recipe has {recipe.Steps.Count} steps; at most {MAX_STEPS} are allowed.");

        for (var index = 0; index < recipe.Steps.Count; index++)
            ValidateStep(recipe.Steps[index], index + 1, kit, findings);

        if (recipe.Steps.Count > 0
            && !string.IsNullOrEmpty(recipe.Application)
            && recipe.Steps.All(step => step.Utility != recipe.Application))
            findings.Add($"No step runs the application '{recipe.Application}'.");

        return findings;
    }

    /// <summary>
    /// Whether a path-like value would leave the case directory: absolute,
    /// drive-rooted, or with a '..' segment.
    /// </summary>
    /// <param name="value">An argument value.</param>
    /// <returns>True when it escapes.</returns>
    public static bool IsPathEscape(string value)
    {
        if (value.StartsWith('/') || value.StartsWith('\\'))
            return true;
        if (value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':')
            return true;

        return value.Split('/', '\\').Any(segment => segment == "..");
    }

    private static void ValidateStep(FoamStepData step, int number, FoamKit? kit, List<string> findings)
    {
        var name = step.Utility;
        var prefix = $"Step {number}";

        if (string.IsNullOrEmpty(name))
        {
            findings.Add($"{prefix} names no utility.");
            return;
        }

        if (!WORD.IsMatch(name))
        {
            findings.Add($"{prefix}: '{name}' is not a utility name.");
            return;
        }

        var allowed = FoamAllowList.IsUtility(name) || FoamAllowList.IsSolverName(name);
        if (!allowed)
        {
            findings.Add($"{prefix}: '{name}' is not on the allow-list.");
            return;
        }

        if (kit != null && !kit.HasExecutable(name))
            findings.Add($"{prefix}: the kit has no '{name}'.");

        if (step.Parallel && !FoamAllowList.IsParallelCapable(name))
            findings.Add($"{prefix}: '{name}' does not run in parallel.");

        foreach (var argument in step.Arguments)
        {
            if (argument.Length == 0)
            {
                findings.Add($"{prefix} ({name}): an empty argument.");
                continue;
            }

            if (NUMBER.IsMatch(argument))
                continue;

            if (argument.StartsWith('-'))
            {
                if (!FLAG.IsMatch(argument))
                    findings.Add($"{prefix} ({name}): '{argument}' is not a flag the allow-list knows.");
                else if (FoamAllowList.IsForbiddenFlag(argument))
                    findings.Add($"{prefix} ({name}): '{argument}' is decided by the controller and may not be given.");
                else if (argument == "-parallel")
                    findings.Add($"{prefix} ({name}): '-parallel' is expressed by the step's parallel flag, not as an argument.");
                continue;
            }

            if (!VALUE.IsMatch(argument))
                findings.Add($"{prefix} ({name}): '{argument}' is not a value the allow-list accepts.");
            else if (IsPathEscape(argument))
                findings.Add($"{prefix} ({name}): '{argument}' points outside the case directory.");
        }
    }

    #endregion
}
