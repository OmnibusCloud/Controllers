using System.Text;
using System.Text.RegularExpressions;
using OutWit.Controller.OpenFOAM.Model;

namespace OutWit.Controller.OpenFOAM.Extraction;

/// <summary>
/// Writes the requested responses into the case as function-object
/// dictionaries, one file per response at <c>system/&lt;Name&gt;</c> - where
/// OpenFOAM's <c>postProcess -func &lt;Name&gt;</c> (and a solver's
/// <c>-postProcess</c> form, for the quantities that need the turbulence
/// model) looks first, before its own <c>etc/caseDicts</c>. The user's files
/// are never edited: a response is a file of its own beside them, and the
/// recipe's post step names it.
/// </summary>
public static class FoamFunctionObjectWriter
{
    #region Constants

    private static readonly Regex WORD = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OPERATION = new("^[A-Za-z][A-Za-z0-9]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A dictionary entry value: words, numbers, vectors and lists in parentheses - no braces, no semicolons, no code.</summary>
    private static readonly Regex VALUE = new(@"^[A-Za-z0-9_.,:+\-eE() \t]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const string PROBE_LOCATIONS = "probeLocations";

    #endregion

    #region Functions

    /// <summary>
    /// Validates the request and writes its function objects.
    /// </summary>
    /// <param name="caseDirectory">The materialised case.</param>
    /// <param name="request">The request; null writes nothing.</param>
    /// <returns>Findings, one sentence each; nothing is written when there is any.</returns>
    public static IReadOnlyList<string> Write(string caseDirectory, FoamExtractionRequestData? request)
    {
        if (request == null || request.Responses.Count == 0)
            return [];

        var findings = Validate(request).ToList();
        var system = Path.Combine(caseDirectory, "system");

        // A response never overwrites a file the user shipped under the same name.
        foreach (var response in request.Responses)
        {
            if (File.Exists(Path.Combine(system, response.Name)))
                findings.Add($"Response '{response.Name}': the case already carries system/{response.Name}; choose another response name.");
        }

        if (findings.Count > 0)
            return findings;

        Directory.CreateDirectory(system);
        foreach (var response in request.Responses)
            File.WriteAllText(Path.Combine(system, response.Name), Render(response), new UTF8Encoding(false));

        return findings;
    }

    /// <summary>
    /// Validates a request without writing.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>Findings, one sentence each.</returns>
    public static IReadOnlyList<string> Validate(FoamExtractionRequestData request)
    {
        var findings = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);

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

            foreach (var patch in response.Patches.Where(patch => !WORD.IsMatch(patch) && !IsQuotedRegex(patch)))
                findings.Add($"{prefix}: '{patch}' is not a patch name.");
            foreach (var field in response.Fields.Where(field => !WORD.IsMatch(field) && field != "U" && field != "p"))
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
    /// The dictionary text of one response.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <returns>A complete function-object dictionary.</returns>
    public static string Render(FoamResponseSpecData response)
    {
        var text = new StringBuilder();
        text.Append("// Written by the OmnibusCloud OpenFOAM controller from the response request; the case's own files are untouched.\n");

        switch (response.Kind)
        {
            case FoamResponseKind.ForceCoeffs:
                text.Append("type            forceCoeffs;\nlibs            (forces);\n");
                text.Append(List("patches", response.Patches));
                text.Append("log             false;\nwriteControl    timeStep;\nwriteInterval   1;\n");
                break;

            case FoamResponseKind.Forces:
                text.Append("type            forces;\nlibs            (forces);\n");
                text.Append(List("patches", response.Patches));
                text.Append("log             false;\nwriteControl    timeStep;\nwriteInterval   1;\n");
                break;

            case FoamResponseKind.PatchValue:
                text.Append("type            surfaceFieldValue;\nlibs            (fieldFunctionObjects);\nregionType      patch;\n");
                text.Append($"name            {response.Patches[0]};\n");
                text.Append($"operation       {response.Operation};\n");
                text.Append(List("fields", response.Fields));
                text.Append("writeFields     false;\nlog             false;\nwriteControl    timeStep;\nwriteInterval   1;\n");
                break;

            case FoamResponseKind.VolumeValue:
                text.Append("type            volFieldValue;\nlibs            (fieldFunctionObjects);\n");
                text.Append(response.Parameters.Any(parameter => parameter.Name == "name")
                    ? "regionType      cellZone;\n"
                    : "regionType      all;\n");
                text.Append($"operation       {response.Operation};\n");
                text.Append(List("fields", response.Fields));
                text.Append("writeFields     false;\nlog             false;\nwriteControl    timeStep;\nwriteInterval   1;\n");
                break;

            case FoamResponseKind.FieldMinMax:
                text.Append("type            fieldMinMax;\nlibs            (fieldFunctionObjects);\nmode            magnitude;\nlocation        false;\n");
                text.Append(List("fields", response.Fields));
                text.Append("log             false;\nwriteControl    timeStep;\nwriteInterval   1;\n");
                break;

            case FoamResponseKind.Probe:
                text.Append("type            probes;\nlibs            (sampling);\n");
                text.Append(List("fields", response.Fields));
                text.Append("log             false;\nwriteControl    timeStep;\nwriteInterval   1;\n");
                break;
        }

        foreach (var parameter in response.Parameters)
            text.Append($"{parameter.Name,-16}{parameter.Value};\n");

        return text.ToString();
    }

    private static string List(string keyword, IReadOnlyList<string> items)
    {
        return $"{keyword,-16}({string.Join(' ', items)});\n";
    }

    private static bool IsQuotedRegex(string patch)
    {
        // OpenFOAM accepts a quoted regular expression as a patch selector:
        // "(motorBike|wall).*" - letters, digits and the regex characters, no
        // whitespace, no code.
        return patch.Length > 2 && patch[0] == '"' && patch[^1] == '"'
               && Regex.IsMatch(patch[1..^1], @"^[A-Za-z0-9_.|*+?()\[\]^$\-]+$");
    }

    #endregion
}
