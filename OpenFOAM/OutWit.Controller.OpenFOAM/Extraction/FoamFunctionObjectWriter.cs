using System.Text;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Model.Rules;

namespace OutWit.Controller.OpenFOAM.Extraction;

/// <summary>
/// Writes the requested responses into the case as function-object
/// dictionaries, one file per response at <c>system/&lt;Name&gt;</c> - where
/// OpenFOAM's <c>postProcess -func &lt;Name&gt;</c> (and a solver's
/// <c>-postProcess</c> form, for the quantities that need the turbulence
/// model) looks first, before its own <c>etc/caseDicts</c>. The user's files
/// are never edited: a response is a file of its own beside them, and the
/// recipe's post step names it. What a request may say is
/// <see cref="FoamResponseRules"/>; this class only renders it.
/// </summary>
public static class FoamFunctionObjectWriter
{
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

        var findings = FoamResponseRules.Validate(request).ToList();
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

    #endregion
}
