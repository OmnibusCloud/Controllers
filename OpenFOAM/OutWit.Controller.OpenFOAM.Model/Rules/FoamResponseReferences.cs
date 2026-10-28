using System.Text.RegularExpressions;

namespace OutWit.Controller.OpenFOAM.Model.Rules;

/// <summary>
/// A force response that re-measures one of the case's own force function
/// objects follows the case: an initiator offers a case's <c>forces</c> and
/// <c>forceCoeffs</c> as responses with their reference values as written,
/// under the function object's name or that name with <see cref="OWN_NAME_SUFFIX"/>
/// when the name is taken, and the node writes the response as a function
/// object of its own. A study that sweeps one of those values (<c>lRef</c>,
/// <c>Aref</c>, <c>rhoInf</c>, <c>magUInf</c>, ...) changes the case's
/// entry in every variant while the response kept the value it was offered
/// with, so the re-measured force was normalised by the original value
/// (K4, 2026-10-28). Here the entries the study sweeps - the ones whose
/// templated text carries a token, a <c>$</c> reference to such an entry
/// included - take the variant's value in the response before the node
/// writes it. The same code tells an initiator which parameters follow.
/// </summary>
public static class FoamResponseReferences
{
    #region Constants

    /// <summary>What an initiator appends to a case function object's name when the name itself is taken.</summary>
    public const string OWN_NAME_SUFFIX = "_sweep";

    /// <summary>The entries of a force function object a response carries as parameters, in the order they are written.</summary>
    public static readonly IReadOnlyList<string> REFERENCE_KEYS =
    [
        FoamResponseRules.DENSITY, FoamResponseRules.FREESTREAM_DENSITY, FoamResponseRules.REFERENCE_SPEED,
        FoamResponseRules.REFERENCE_LENGTH, FoamResponseRules.REFERENCE_AREA,
        FoamResponseRules.CENTRE_OF_ROTATION, FoamResponseRules.ORIGIN, FoamResponseRules.COORDINATE_SYSTEM,
        FoamResponseRules.LIFT_DIRECTION, FoamResponseRules.DRAG_DIRECTION, "pitchAxis"
    ];

    /// <summary>The function-object types a response of each force kind re-measures.</summary>
    private static readonly IReadOnlyDictionary<FoamResponseKind, string> TYPES = new Dictionary<FoamResponseKind, string>
    {
        [FoamResponseKind.Forces] = "forces",
        [FoamResponseKind.ForceCoeffs] = "forceCoeffs"
    };

    /// <summary>A reference to another entry: <c>$name</c> or <c>${name}</c>, alone.</summary>
    private static readonly Regex REFERENCE = new(@"^\$\{?([A-Za-z_][A-Za-z0-9_]*)\}?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>How many references are followed before the chain is given up as circular.</summary>
    private const int MAX_REFERENCE_DEPTH = 8;

    #endregion

    #region Functions

    /// <summary>
    /// The case function object a response re-measures: the one of the
    /// response's kind named as the response, or as the response without
    /// <see cref="OWN_NAME_SUFFIX"/>.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <param name="functions">The case's function objects (<see cref="FoamCaseFunctionObjects.Enumerate"/>).</param>
    /// <returns>The function object, or null when the response is not one of the case's.</returns>
    public static FoamCaseFunctionObject? ReMeasured(FoamResponseSpecData response, IReadOnlyList<FoamCaseFunctionObject> functions)
    {
        if (!TYPES.TryGetValue(response.Kind, out var type))
            return null;

        var names = new List<string> { response.Name };
        if (response.Name.EndsWith(OWN_NAME_SUFFIX, StringComparison.Ordinal) && response.Name.Length > OWN_NAME_SUFFIX.Length)
            names.Add(response.Name[..^OWN_NAME_SUFFIX.Length]);

        return names
            .Select(name => functions.FirstOrDefault(function => function.Name == name && function.Type == type))
            .FirstOrDefault(function => function != null);
    }

    /// <summary>
    /// An entry of a function object with its <c>$</c> references followed:
    /// in the function object's own body first, then at the top level of
    /// the controlDict, as OpenFOAM looks a reference up through the
    /// enclosing dictionaries.
    /// </summary>
    /// <param name="function">The function object.</param>
    /// <param name="keyword">The entry's keyword.</param>
    /// <param name="controlDictText">The controlDict's text, for a reference to a top-level entry.</param>
    /// <returns>The value as written where it ends, or null when the entry is absent or the chain breaks.</returns>
    public static string? Resolve(FoamCaseFunctionObject function, string keyword, string controlDictText)
    {
        var value = function.Entry(keyword);
        for (var depth = 0; value != null && depth < MAX_REFERENCE_DEPTH; depth++)
        {
            var reference = REFERENCE.Match(value);
            if (!reference.Success)
                return value;

            var name = reference.Groups[1].Value;
            value = function.Entry(name) ?? FoamCaseFunctionObjects.Entry(controlDictText, name);
        }

        return value != null && REFERENCE.IsMatch(value) ? null : value;
    }

    /// <summary>
    /// A copy of the request in which every parameter of a re-measured
    /// force that the study sweeps carries the variant's value: an entry
    /// whose templated text (references followed) carries a token takes the
    /// instantiated text of the same entry; a parameter the response did
    /// not carry is added. A parameter the study does not sweep keeps what
    /// the request says.
    /// </summary>
    /// <param name="request">The request; null gives null.</param>
    /// <param name="templated">The case's function objects read from the templated files, tokens in place.</param>
    /// <param name="templatedControlDict">The templated controlDict's text.</param>
    /// <param name="instantiated">The same function objects read from the variant's files.</param>
    /// <param name="instantiatedControlDict">The variant's controlDict text.</param>
    /// <param name="notes">Receives one sentence per parameter that follows; null discards them.</param>
    /// <returns>The request to write; the given one is left as it is.</returns>
    public static FoamExtractionRequestData? Follow(
        FoamExtractionRequestData? request,
        IReadOnlyList<FoamCaseFunctionObject> templated,
        string templatedControlDict,
        IReadOnlyList<FoamCaseFunctionObject> instantiated,
        string instantiatedControlDict,
        ICollection<string>? notes = null)
    {
        if (request == null)
            return null;

        var followed = request.Clone();
        foreach (var response in followed.Responses)
        {
            var original = ReMeasured(response, templated);
            if (original == null)
                continue;

            var variant = instantiated.FirstOrDefault(function => function.Name == original.Name && function.Type == original.Type);
            if (variant == null)
                continue;

            foreach (var key in REFERENCE_KEYS)
            {
                var templatedValue = Resolve(original, key, templatedControlDict);
                if (templatedValue == null || !FoamTemplating.TOKEN.IsMatch(templatedValue))
                    continue;

                var value = Resolve(variant, key, instantiatedControlDict);
                if (value == null)
                    continue;

                var parameter = response.Parameters.FirstOrDefault(parameter => parameter.Name == key);
                if (parameter == null)
                    response.Parameters.Add(new FoamNamedValueData { Name = key, Value = value });
                else
                    parameter.Value = value;

                notes?.Add($"Response '{response.Name}': {key} follows the case's {original.Name} ({original.RelativePath}:{original.Line}), which the study sweeps - {value} in this variant.");
            }
        }

        return followed;
    }

    #endregion
}
