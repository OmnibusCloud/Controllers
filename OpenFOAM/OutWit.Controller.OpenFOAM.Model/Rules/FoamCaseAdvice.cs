using System.Text.RegularExpressions;

namespace OutWit.Controller.OpenFOAM.Model.Rules;

/// <summary>
/// What a task would do without doing what was meant - not refused, said:
/// ranks asked for while no step of the recipe runs in parallel (every
/// step then runs serially on one rank, the ranks do nothing); a
/// <c>setFields</c> step that overwrites an <c>internalField</c> the study
/// sweeps (<c>defaultFieldValues</c> everywhere, a region's
/// <c>fieldValues</c> inside it), so the swept value has no effect or holds
/// outside the regions only - the token written in the entry itself, or
/// reached through a <c>$</c> reference to an entry of the file or of a
/// file it <c>#include</c>s (motorBike's <c>internalField uniform
/// $flowVelocity</c>, the velocity written in <c>0/include/initialConditions</c>).
/// An initiator shows the sentences at preflight; the node writes them
/// into <c>log.responses</c> beside the run's notes.
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

    /// <summary>The whole <c>defaultFieldValues</c> list, a vector value's parentheses inside it: the class leaves <c>(</c> to the inner group, or a scalar after a vector (<c>U (0 0 0) volScalarFieldValue p 0</c>) would fall outside the list.</summary>
    private static readonly Regex DEFAULT_VALUES = new(@"(?<![A-Za-z0-9_])defaultFieldValues\s*\(([^()]*(?:\([^()]*\)[^()]*)*)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LINE_COMMENT = new(@"//[^\n]*", RegexOptions.Compiled);

    private static readonly Regex BLOCK_COMMENT = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>A reference to another entry inside a value: <c>$name</c> or <c>${name}</c>.</summary>
    private static readonly Regex REFERENCE = new(@"\$\{?([A-Za-z_][A-Za-z0-9_]*)\}?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>How many times a value's references are followed before the chain is given up as circular.</summary>
    private const int MAX_REFERENCE_DEPTH = 8;

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
    /// <param name="files">The case's files that matter, by relative path (forward slashes) with their templated text, tokens in place: <c>system/setFieldsDict</c>, the initial fields and the files they <c>#include</c>; a file not given is not read.</param>
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

        // The files by path, comments blanked, for the references a value follows.
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (relativePath, text) in files)
        {
            if (text != null)
                texts.TryAdd(relativePath, StripComments(text));
        }

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

            var value = Resolve(internalField.Groups[1].Value, relativePath, texts, out var through);
            var token = FoamTemplating.TOKEN.Match(value);
            if (!token.Success)
                continue;

            if (everywhere.Contains(field))
                advice.Add($"{relativePath}: internalField carries {token.Value}{through}, which the study sweeps, and the recipe's {SET_FIELDS} step overwrites it everywhere ({SET_FIELDS_DICT} defaultFieldValues names {field}): the swept value has no effect.");
            else if (inRegions.Contains(field))
                advice.Add($"{relativePath}: internalField carries {token.Value}{through}, which the study sweeps, and the recipe's {SET_FIELDS} step overwrites it inside the regions of {SET_FIELDS_DICT}: the swept value holds outside them only.");
        }

        return advice;
    }

    #endregion

    #region Tools

    // The value with its $ references followed, as OpenFOAM looks an entry up
    // through the enclosing dictionary: the field file's top level, the
    // entries of the files it #includes (and theirs) among them. A reference
    // no file answers stays as written. Says which reference brought the
    // token and from which file - " through $flowVelocity of 0/include/initialConditions",
    // the file left out when it is the field's own - or nothing when the
    // token is written in the entry itself.
    private static string Resolve(string value, string relativePath, IReadOnlyDictionary<string, string> texts, out string through)
    {
        through = string.Empty;
        if (FoamTemplating.TOKEN.IsMatch(value))
            return value;

        var origin = string.Empty;
        for (var depth = 0; depth < MAX_REFERENCE_DEPTH && REFERENCE.IsMatch(value); depth++)
        {
            var followed = false;
            value = REFERENCE.Replace(value, match =>
            {
                var entry = Lookup(match.Groups[1].Value, relativePath, texts, new HashSet<string>(StringComparer.Ordinal), out var file);
                if (entry == null)
                    return match.Value;

                followed = true;
                if (origin.Length == 0 && FoamTemplating.TOKEN.IsMatch(entry))
                    origin = file == relativePath ? $" through {match.Value}" : $" through {match.Value} of {file}";

                return entry;
            });

            if (!followed)
                break;
        }

        if (FoamTemplating.TOKEN.IsMatch(value))
            through = origin;

        return value;
    }

    // An entry at the top level of a file, or of a file it #includes, and so
    // on; each file read once along a chain.
    private static string? Lookup(string name, string relativePath, IReadOnlyDictionary<string, string> texts, HashSet<string> visited, out string file)
    {
        file = relativePath;
        if (!visited.Add(relativePath) || !texts.TryGetValue(relativePath, out var text))
            return null;

        var entry = FoamCaseFunctionObjects.Entry(text, name);
        if (entry != null)
            return entry;

        foreach (var included in FoamCaseFunctionObjects.IncludedFiles(text, relativePath))
        {
            entry = Lookup(name, included, texts, visited, out file);
            if (entry != null)
                return entry;
        }

        return null;
    }

    private static string StripComments(string text)
    {
        text = BLOCK_COMMENT.Replace(text, " ");
        return LINE_COMMENT.Replace(text, " ");
    }

    #endregion
}
