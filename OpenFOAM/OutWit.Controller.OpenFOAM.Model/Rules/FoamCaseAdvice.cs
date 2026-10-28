using System.Text.RegularExpressions;

namespace OutWit.Controller.OpenFOAM.Model.Rules;

/// <summary>
/// What a task would do without doing what was meant - not refused, said:
/// ranks asked for while no step of the recipe runs in parallel (every
/// step then runs serially on one rank, the ranks do nothing); a
/// <c>setFields</c> step that overwrites an <c>internalField</c> the study
/// sweeps (<c>defaultFieldValues</c> everywhere, a region's
/// <c>fieldValues</c> inside it), so the swept value has no effect or holds
/// outside the regions only. An initiator shows the sentences at preflight;
/// the node writes them into <c>log.responses</c> beside the run's notes.
/// </summary>
public static class FoamCaseAdvice
{
    #region Constants

    private const string SET_FIELDS = "setFields";

    private const string SET_FIELDS_DICT = "system/setFieldsDict";

    private static readonly string[] INITIAL_FOLDERS = ["0/", "0.orig/"];

    /// <summary>An internalField entry with its value up to the semicolon.</summary>
    private static readonly Regex INTERNAL_FIELD = new(@"(?<![A-Za-z0-9_])internalField\s+([^;]*);", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A field a setFields value names: <c>volScalarFieldValue alpha.water 0</c>, <c>volVectorFieldValue U (0 0 0)</c>.</summary>
    private static readonly Regex FIELD_VALUE = new(@"(?<![A-Za-z0-9_])vol[A-Za-z]+FieldValue\s+([A-Za-z][A-Za-z0-9_.]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DEFAULT_VALUES = new(@"(?<![A-Za-z0-9_])defaultFieldValues\s*\(([^)]*(?:\([^)]*\)[^)]*)*)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LINE_COMMENT = new(@"//[^\n]*", RegexOptions.Compiled);

    private static readonly Regex BLOCK_COMMENT = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

    #endregion

    #region Functions

    /// <summary>
    /// The advice a case's data alone gives: ranks without a parallel step.
    /// </summary>
    /// <param name="data">The case; null gives nothing.</param>
    /// <returns>Sentences, one each; empty when the data says nothing.</returns>
    public static IReadOnlyList<string> Of(FoamCaseData? data)
    {
        if (data?.Recipe == null || data.Threads <= 1 || data.Recipe.Steps.Any(step => step.Parallel))
            return [];

        return
        [
            $"The task asks for {data.Threads} ranks, and no step of the recipe runs in parallel: every step runs serially on one rank, and the other {data.Threads - 1} do nothing. Mark the solve (and the steps that may) parallel, or ask for one thread."
        ];
    }

    /// <summary>
    /// The advice the templated files give: a <c>setFields</c> step over an
    /// <c>internalField</c> the study sweeps.
    /// </summary>
    /// <param name="recipe">The recipe; without a <c>setFields</c> step there is nothing to say.</param>
    /// <param name="files">The case's files that matter, by relative path (forward slashes) with their templated text, tokens in place: <c>system/setFieldsDict</c> and the initial fields; a file not given is not read.</param>
    /// <returns>Sentences, one per swept field the step overwrites; empty when there is nothing to say.</returns>
    public static IReadOnlyList<string> OfSetFields(FoamRecipeData? recipe, IReadOnlyList<(string RelativePath, string? Text)> files)
    {
        var advice = new List<string>();
        if (recipe == null || recipe.Steps.All(step => step.Utility != SET_FIELDS))
            return advice;

        var dictionary = files.FirstOrDefault(file => file.RelativePath == SET_FIELDS_DICT).Text;
        if (dictionary == null)
            return advice;

        dictionary = StripComments(dictionary);
        var everywhere = DEFAULT_VALUES.Matches(dictionary)
            .SelectMany(match => FIELD_VALUE.Matches(match.Groups[1].Value).Select(field => field.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);
        var inRegions = FIELD_VALUE.Matches(dictionary).Select(match => match.Groups[1].Value).Where(field => !everywhere.Contains(field)).ToHashSet(StringComparer.Ordinal);

        foreach (var (relativePath, text) in files)
        {
            if (text == null || !INITIAL_FOLDERS.Any(folder => relativePath.StartsWith(folder, StringComparison.Ordinal)))
                continue;

            var field = relativePath[(relativePath.IndexOf('/') + 1)..];
            if (field.Contains('/'))
                continue;

            var internalField = INTERNAL_FIELD.Match(StripComments(text));
            if (!internalField.Success)
                continue;

            var token = FoamTemplating.TOKEN.Match(internalField.Groups[1].Value);
            if (!token.Success)
                continue;

            if (everywhere.Contains(field))
                advice.Add($"{relativePath}: internalField carries {token.Value}, which the study sweeps, and the recipe's {SET_FIELDS} step overwrites it everywhere ({SET_FIELDS_DICT} defaultFieldValues names {field}): the swept value has no effect.");
            else if (inRegions.Contains(field))
                advice.Add($"{relativePath}: internalField carries {token.Value}, which the study sweeps, and the recipe's {SET_FIELDS} step overwrites it inside the regions of {SET_FIELDS_DICT}: the swept value holds outside them only.");
        }

        return advice;
    }

    #endregion

    #region Tools

    private static string StripComments(string text)
    {
        text = BLOCK_COMMENT.Replace(text, " ");
        return LINE_COMMENT.Replace(text, " ");
    }

    #endregion
}
