using System.Text.RegularExpressions;

namespace OutWit.Controller.OpenFOAM.Model.Rules;

/// <summary>
/// The rules a recipe obeys: every step against the allow-list and the
/// argument grammar, the application a solver that some step runs. The same
/// code runs on the node before the first process starts, in the Sweep host
/// when a study is planned and in the initiator's preflight, so a user reads
/// the same sentence wherever a recipe is refused. Whether the kit carries an
/// executable is asked through a predicate: the rules know no kit.
/// </summary>
public static class FoamRecipeRules
{
    #region Constants

    /// <summary>Upper bound on the steps of one recipe; a longer one is a script, not a recipe.</summary>
    public const int MAX_STEPS = 32;

    /// <summary>A flag: a dash, a letter, then letters, digits or dashes.</summary>
    private static readonly Regex FLAG = new("^-[A-Za-z][A-Za-z0-9-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// A value: OpenFOAM words, numbers, relative paths, lists in parentheses
    /// (<c>(nonOrthoAngle)</c>) and comma lists - no shell metacharacters, no
    /// quotes, no whitespace.
    /// </summary>
    private static readonly Regex VALUE = new(@"^[A-Za-z0-9_(][A-Za-z0-9_.,:=+()/\-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WORD = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A number, which may start with a minus and is a value, not a flag (<c>-time -1</c>).</summary>
    private static readonly Regex NUMBER = new(@"^-?\d+(\.\d+)?([eE][-+]?\d+)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    #endregion

    #region Functions

    /// <summary>
    /// Validates a recipe.
    /// </summary>
    /// <param name="recipe">The recipe; null is a finding.</param>
    /// <param name="hasExecutable">Answers whether the kit the steps will run under carries an executable; null skips the kit checks.</param>
    /// <returns>Findings, one sentence each; empty when the recipe may run.</returns>
    public static IReadOnlyList<string> Validate(FoamRecipeData? recipe, Func<string, bool>? hasExecutable = null)
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
        else if (hasExecutable != null && !hasExecutable(recipe.Application))
            findings.Add($"The kit has no solver '{recipe.Application}'.");

        if (recipe.Steps.Count == 0)
            findings.Add("The recipe has no steps.");
        if (recipe.Steps.Count > MAX_STEPS)
            findings.Add($"The recipe has {recipe.Steps.Count} steps; at most {MAX_STEPS} are allowed.");

        for (var index = 0; index < recipe.Steps.Count; index++)
            ValidateStep(recipe.Steps[index], $"Step {index + 1}", hasExecutable, findings);

        if (recipe.Steps.Count > 0
            && !string.IsNullOrEmpty(recipe.Application)
            && recipe.Steps.All(step => step.Utility != recipe.Application))
            findings.Add($"No step runs the application '{recipe.Application}'.");

        return findings;
    }

    /// <summary>
    /// Validates one step on its own, in the words <see cref="Validate"/>
    /// uses, under the caller's name for it: a recipe says <c>Step 3</c>, a
    /// script translated into steps names its file and line.
    /// </summary>
    /// <param name="step">The step.</param>
    /// <param name="prefix">What each finding starts with (<c>Allrun:12</c>).</param>
    /// <param name="hasExecutable">Answers whether the kit carries an executable; null skips the kit check.</param>
    /// <returns>Findings, one sentence each; empty when the step may run.</returns>
    public static IReadOnlyList<string> ValidateStep(FoamStepData step, string prefix, Func<string, bool>? hasExecutable = null)
    {
        var findings = new List<string>();
        ValidateStep(step, prefix, hasExecutable, findings);
        return findings;
    }

    private static void ValidateStep(FoamStepData step, string prefix, Func<string, bool>? hasExecutable, List<string> findings)
    {
        var name = step.Utility;

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

        if (hasExecutable != null && !hasExecutable(name))
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
            else if (FoamCasePathRules.IsPathEscape(argument))
                findings.Add($"{prefix} ({name}): '{argument}' points outside the case directory.");
        }
    }

    #endregion
}
