using System.Text.RegularExpressions;

namespace OutWit.Controller.OpenFOAM.Model.Rules;

/// <summary>
/// The rules a response request obeys before the node writes it into the
/// case as function objects under <c>system/&lt;Name&gt;</c>: names that are
/// words and unique, the parts each kind needs, patch, field and parameter
/// text that is plain dictionary text and never code, and no response named
/// like a file the case already ships under <c>system/</c> (a response never
/// overwrites the user's file).
/// </summary>
public static class FoamResponseRules
{
    #region Constants

    /// <summary>The parameter a probe response must carry.</summary>
    public const string PROBE_LOCATIONS = "probeLocations";

    private const string SYSTEM_PREFIX = "system/";

    private static readonly Regex WORD = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OPERATION = new("^[A-Za-z][A-Za-z0-9]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A dictionary entry value: words, numbers, vectors and lists in parentheses - no braces, no semicolons, no code.</summary>
    private static readonly Regex VALUE = new(@"^[A-Za-z0-9_.,:+\-eE() \t]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A quoted patch selector's body: letters, digits and the regular-expression characters, no whitespace, no code.</summary>
    private static readonly Regex PATCH_REGEX = new(@"^[A-Za-z0-9_.|*+?()\[\]^$\-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    #endregion

    #region Functions

    /// <summary>
    /// Validates a request.
    /// </summary>
    /// <param name="request">The request; null has nothing to validate.</param>
    /// <param name="caseFiles">The case's relative file paths, to refuse a response named like a file under <c>system/</c>; null skips that check.</param>
    /// <returns>Findings, one sentence each.</returns>
    public static IReadOnlyList<string> Validate(FoamExtractionRequestData? request, IReadOnlyCollection<string>? caseFiles = null)
    {
        var findings = new List<string>();
        if (request == null)
            return findings;

        var names = new HashSet<string>(StringComparer.Ordinal);
        var systemFiles = new HashSet<string>(
            (caseFiles ?? []).Where(path => path.StartsWith(SYSTEM_PREFIX, StringComparison.Ordinal)).Select(path => path[SYSTEM_PREFIX.Length..]),
            StringComparer.Ordinal);

        foreach (var response in request.Responses)
        {
            var prefix = $"Response '{response.Name}'";

            if (!WORD.IsMatch(response.Name))
            {
                findings.Add($"Response name '{response.Name}' is not a word (letters, digits, underscore).");
                continue;
            }

            if (!names.Add(response.Name))
                findings.Add($"{prefix} is requested twice.");
            if (systemFiles.Contains(response.Name))
                findings.Add($"{prefix}: the case already carries system/{response.Name}; choose another response name.");

            ValidateKind(response, prefix, findings);

            foreach (var patch in response.Patches.Where(patch => !WORD.IsMatch(patch) && !IsQuotedRegex(patch)))
                findings.Add($"{prefix}: '{patch}' is not a patch name.");
            foreach (var field in response.Fields.Where(field => !WORD.IsMatch(field)))
                findings.Add($"{prefix}: '{field}' is not a field name.");
            foreach (var parameter in response.Parameters)
            {
                if (!WORD.IsMatch(parameter.Name))
                    findings.Add($"{prefix}: parameter '{parameter.Name}' is not a keyword.");
                else if (!VALUE.IsMatch(parameter.Value))
                    findings.Add($"{prefix}: the value of '{parameter.Name}' is not a plain dictionary value.");
            }
        }

        return findings;
    }

    /// <summary>
    /// Whether a patch entry is a quoted regular expression OpenFOAM accepts as a selector (<c>"(motorBike|wall).*"</c>).
    /// </summary>
    /// <param name="patch">The patch entry.</param>
    /// <returns>True for a quoted selector of plain regular-expression text.</returns>
    public static bool IsQuotedRegex(string patch)
    {
        return patch.Length > 2 && patch[0] == '"' && patch[^1] == '"' && PATCH_REGEX.IsMatch(patch[1..^1]);
    }

    private static void ValidateKind(FoamResponseSpecData response, string prefix, List<string> findings)
    {
        switch (response.Kind)
        {
            case FoamResponseKind.ForceCoeffs:
            case FoamResponseKind.Forces:
                if (response.Patches.Count == 0)
                    findings.Add($"{prefix}: {response.Kind} needs at least one patch.");
                break;

            case FoamResponseKind.PatchValue:
                if (response.Patches.Count != 1)
                    findings.Add($"{prefix}: a patch value names exactly one patch (request one response per patch).");
                if (response.Fields.Count == 0)
                    findings.Add($"{prefix}: a patch value needs at least one field.");
                if (!OPERATION.IsMatch(response.Operation))
                    findings.Add($"{prefix}: '{response.Operation}' is not an operation (areaAverage, areaIntegrate, min, max, ...).");
                break;

            case FoamResponseKind.VolumeValue:
                if (response.Fields.Count == 0)
                    findings.Add($"{prefix}: a volume value needs at least one field.");
                if (!OPERATION.IsMatch(response.Operation))
                    findings.Add($"{prefix}: '{response.Operation}' is not an operation (volAverage, volIntegrate, min, max, ...).");
                break;

            case FoamResponseKind.FieldMinMax:
                if (response.Fields.Count == 0)
                    findings.Add($"{prefix}: a min/max needs at least one field.");
                break;

            case FoamResponseKind.Probe:
                if (response.Fields.Count == 0)
                    findings.Add($"{prefix}: a probe needs at least one field.");
                if (response.Parameters.All(parameter => parameter.Name != PROBE_LOCATIONS))
                    findings.Add($"{prefix}: a probe needs a '{PROBE_LOCATIONS}' parameter.");
                break;

            default:
                findings.Add($"{prefix}: unknown kind {response.Kind}.");
                break;
        }
    }

    #endregion
}
