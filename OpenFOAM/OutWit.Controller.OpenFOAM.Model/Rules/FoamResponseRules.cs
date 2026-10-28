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

    /// <summary>The centre moments are taken about: a force function object does not start without it (or <see cref="ORIGIN"/>).</summary>
    public const string CENTRE_OF_ROTATION = "CofR";

    /// <summary>The coordinate-system spelling of <see cref="CENTRE_OF_ROTATION"/>.</summary>
    public const string ORIGIN = "origin";

    /// <summary>
    /// A coordinate system of the force function object's own, a flat
    /// sub-dictionary (<c>{ origin (0 0 0); e1 (1 0 0); e3 (0 0 1); }</c>):
    /// it carries the centre and the directions, so neither <see cref="CENTRE_OF_ROTATION"/>
    /// nor <see cref="LIFT_DIRECTION"/> and <see cref="DRAG_DIRECTION"/> are read beside it
    /// (v2606 <c>forces::setCoordinateSystem</c>).
    /// </summary>
    public const string COORDINATE_SYSTEM = "coordinateSystem";

    /// <summary>The reference speed of force coefficients.</summary>
    public const string REFERENCE_SPEED = "magUInf";

    /// <summary>The reference length of force coefficients.</summary>
    public const string REFERENCE_LENGTH = "lRef";

    /// <summary>The reference area of force coefficients.</summary>
    public const string REFERENCE_AREA = "Aref";

    /// <summary>
    /// The freestream density. Force coefficients read it whatever <see cref="DENSITY"/>
    /// says (v2606: <c>forces::read</c> under <c>rho rhoInf</c>, <c>forceCoeffs::read</c>
    /// otherwise, both with <c>readEntry</c>); a force reads it under <c>rho rhoInf</c>.
    /// </summary>
    public const string FREESTREAM_DENSITY = "rhoInf";

    /// <summary>The density entry: the name of a density field, or <c>rhoInf</c> for a kinematic case.</summary>
    public const string DENSITY = "rho";

    /// <summary>
    /// The lift direction of force coefficients: given <see cref="CENTRE_OF_ROTATION"/>,
    /// v2606 reads it and <see cref="DRAG_DIRECTION"/> with <c>get</c>, no default.
    /// </summary>
    public const string LIFT_DIRECTION = "liftDir";

    /// <summary>The drag direction of force coefficients, read with <see cref="LIFT_DIRECTION"/>.</summary>
    public const string DRAG_DIRECTION = "dragDir";

    private const string SYSTEM_PREFIX = "system/";

    /// <summary>
    /// What a placeholder counts as when a value is checked before the
    /// variant supplies it: a number. The node checks the value again once it
    /// is substituted.
    /// </summary>
    private const string TOKEN_STAND_IN = "0";

    private static readonly Regex WORD = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// A field: a word that may carry dots, as every multiphase solver names
    /// its phases (<c>alpha.water</c>) and a region its fields. No colon: a
    /// probe writes one file per field, and a Windows node has no file named
    /// <c>U:Transformed</c>.
    /// </summary>
    private static readonly Regex FIELD = new("^[A-Za-z][A-Za-z0-9_.]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OPERATION = new("^[A-Za-z][A-Za-z0-9]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A dictionary entry value: words, numbers, vectors and lists in parentheses - no braces, no semicolons, no code.</summary>
    private static readonly Regex VALUE = new(@"^[A-Za-z0-9_.,:+\-eE() \t]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// A flat sub-dictionary as a value: braces around plain entries, each a
    /// keyword, a <see cref="VALUE"/> and a semicolon - what a
    /// <c>coordinateSystem</c> or a <c>binData</c> is, and no nesting, no code.
    /// </summary>
    private static readonly Regex SUB_DICTIONARY = new(@"^\{(\s*[A-Za-z][A-Za-z0-9_]*\s+[A-Za-z0-9_.,:+\-eE() \t]+;)*\s*\}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
            foreach (var field in response.Fields.Where(field => !FIELD.IsMatch(field)))
                findings.Add($"{prefix}: '{field}' is not a field name.");
            foreach (var parameter in response.Parameters)
            {
                if (!WORD.IsMatch(parameter.Name))
                    findings.Add($"{prefix}: parameter '{parameter.Name}' is not a keyword.");
                else if (!IsPlainValue(FoamTemplating.TOKEN.Replace(parameter.Value, TOKEN_STAND_IN)))
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

                // OpenFOAM stops the solve on a force function object without
                // them ("Entry 'magUInf' not found", "Entry 'origin' not
                // found" - measured on v2606, 2026-10-28).
                var missing = MissingReferences(response);
                if (missing.Count > 0)
                    findings.Add($"{prefix}: {response.Kind} needs {string.Join(", ", missing)} - OpenFOAM does not start the function object without them.");
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

    /// <summary>
    /// The entries a force function object reads without a default, by what
    /// the request already says - measured on v2606 (2026-10-28), each a
    /// "FOAM FATAL IO ERROR ... not found" that stops the solve:
    /// the centre (<see cref="CENTRE_OF_ROTATION"/>, <see cref="ORIGIN"/> or a
    /// <see cref="COORDINATE_SYSTEM"/>); for coefficients <see cref="REFERENCE_SPEED"/>,
    /// <see cref="REFERENCE_LENGTH"/>, <see cref="REFERENCE_AREA"/> and
    /// <see cref="FREESTREAM_DENSITY"/> whatever <see cref="DENSITY"/> says, and
    /// <see cref="LIFT_DIRECTION"/> with <see cref="DRAG_DIRECTION"/> when the
    /// centre is a <see cref="CENTRE_OF_ROTATION"/>; for a force
    /// <see cref="FREESTREAM_DENSITY"/> when <see cref="DENSITY"/> is <c>rhoInf</c>.
    /// </summary>
    /// <param name="response">A force or force-coefficient response.</param>
    /// <returns>The missing entry names, in the order OpenFOAM reads them; empty when the function object starts.</returns>
    public static IReadOnlyList<string> MissingReferences(FoamResponseSpecData response)
    {
        var values = response.Parameters
            .Where(parameter => parameter.Value.Trim().Length > 0)
            .GroupBy(parameter => parameter.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Value.Trim(), StringComparer.Ordinal);

        var missing = new List<string>();
        var hasCoordinateSystem = values.ContainsKey(COORDINATE_SYSTEM);
        var hasCentre = values.ContainsKey(CENTRE_OF_ROTATION);
        if (!hasCoordinateSystem && !hasCentre && !values.ContainsKey(ORIGIN))
            missing.Add(CENTRE_OF_ROTATION);

        var kinematic = values.TryGetValue(DENSITY, out var density) && density == FREESTREAM_DENSITY;

        if (response.Kind == FoamResponseKind.ForceCoeffs)
        {
            missing.AddRange(new[] { REFERENCE_SPEED, REFERENCE_LENGTH, REFERENCE_AREA, FREESTREAM_DENSITY }.Where(name => !values.ContainsKey(name)));
            if (hasCentre && !hasCoordinateSystem)
                missing.AddRange(new[] { LIFT_DIRECTION, DRAG_DIRECTION }.Where(name => !values.ContainsKey(name)));
        }
        else if (kinematic && !values.ContainsKey(FREESTREAM_DENSITY))
        {
            missing.Add(FREESTREAM_DENSITY);
        }

        return missing;
    }

    /// <summary>
    /// Whether a parameter value is plain dictionary text: a word, a number,
    /// a vector or a list in parentheses, or a flat sub-dictionary of such
    /// entries - never braces inside braces, a semicolon outside one, or code.
    /// </summary>
    /// <param name="value">The value text, tokens already stood in for.</param>
    /// <returns>True for a value the node writes verbatim.</returns>
    public static bool IsPlainValue(string value)
    {
        // A blank value is plain text too: the reference check names it as
        // missing, which is the more useful sentence.
        return VALUE.IsMatch(value) || SUB_DICTIONARY.IsMatch(value.Trim());
    }

    #endregion
}
