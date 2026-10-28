using System.Text.RegularExpressions;

namespace OutWit.Controller.OpenFOAM.Model.Rules;

/// <summary>
/// Which function objects a case may carry on a pool node. The kit's own
/// libraries hold function objects that reach the host - <c>systemCall</c>
/// runs any command, <c>timeActivatedFileUpdate</c> copies any file of the
/// node into the case, <c>writeDictionary</c> and the writers put files
/// where their entries say - and compile nothing, so the run-time code
/// rules never see them. A pool node is another person's machine: a case
/// may carry only the function objects named here, which measure and write
/// inside the case, and no entry of theirs may name a path outside the
/// case. The node applies the rule to the materialised case before anything
/// runs and the initiator to the case the user opened - the same code, the
/// same sentences.
/// </summary>
public static class FoamFunctionObjectRules
{
    #region Constants

    /// <summary>
    /// The function objects a case may carry: the measurements and the
    /// field, surface and sampling writers of the kit's own libraries, each
    /// writing under the case's <c>postProcessing/</c> or into a time folder,
    /// none running a command or opening a path of its own.
    /// </summary>
    public static readonly IReadOnlyList<string> ALLOWED =
    [
        // Forces and coefficients.
        "forces", "forceCoeffs", "bladeForces", "propellerInfo", "phaseForces",
        // Field values and statistics.
        "fieldMinMax", "fieldStatistics", "fieldAverage", "fieldExtents", "valueAverage", "volFieldValue",
        "surfaceFieldValue", "multiFieldValue", "histogram", "momentum", "momentumError", "continuityError",
        "flux", "fluxSummary", "interfaceHeight", "regionSizeDistribution", "sizeDistribution",
        // Sampling.
        "probes", "patchProbes", "thermoCoupleProbes", "radiometerProbes", "sets", "surfaces",
        "streamLine", "wallBoundedStreamLine", "nearWallFields",
        // Derived fields.
        "add", "subtract", "multiply", "divide", "pow", "mag", "magSqr", "components", "norm", "randomise",
        "grad", "div", "ddt", "ddt2", "curl", "vorticity", "enstrophy", "Lambda2", "Q", "LambVector", "flowType",
        "pressure", "hydrostaticPressure", "CourantNo", "surfaceCourantNumber", "MachNo", "PecletNo",
        "wallShearStress", "wallHeatFlux", "yPlus", "heatTransferCoeff", "turbulenceFields", "DESModelRegions",
        "blendingFactor", "stabilityBlendingFactor", "derivedFields", "exprField", "age", "comfort",
        "columnAverage", "fieldCoordinateSystemTransform", "limitFields", "zeroGradient", "reference",
        "resolutionIndex", "streamFunction", "surfaceInterpolate", "surfaceDistance", "writeCellCentres", "writeCellVolumes",
        "binField", "energySpectrum", "proudmanAcousticPower", "Curle", "XiReactionRate", "BilgerMixtureFraction",
        "electricPotential", "energyTransport", "scalarTransport", "AMIWeights", "processorField", "DMD",
        "psiReactionThermoMoleFractions", "rhoReactionThermoMoleFractions", "psiSpecieReactionRates", "rhoSpecieReactionRates",
        "psiReactionsSensitivityAnalysis", "rhoReactionsSensitivityAnalysis",
        // Heat transfer and phases.
        "multiphaseInterHtcModel", "reactingEulerHtcModel", "viewFactorHeatFlux",
        // Lagrangian.
        "cloudInfo", "dataCloud", "particleDistribution", "extractEulerianParticles", "icoUncoupledKinematicCloud", "dsmcFields",
        // Writers into the case.
        "writeObjects", "readFields", "removeRegisteredObject", "ensightWrite", "ensightCloud", "vtkWrite", "vtkCloud", "areaWrite",
        // Run control and reports.
        "runTimeControl", "timeInfo", "solverInfo", "caseInfo", "foamReport", "log", "setTimeStep", "setTimeStepFaRegion",
        "setFlow", "syncObjects", "agglomerationInfo", "parProfiling", "cellDecomposer", "mapFields", "multiRegion",
        "relativeVelocity", "graphFunctionObject"
    ];

    /// <summary>
    /// What a function object the list does not name would do: the reason the
    /// sentence gives for the ones the kit carries.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> REFUSED_BECAUSE = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["systemCall"] = "runs commands on the node",
        ["timeActivatedFileUpdate"] = "copies a file of the node into the case",
        ["writeDictionary"] = "writes a dictionary where its entries say",
        ["externalCoupled"] = "exchanges files with a program on the node",
        ["abort"] = "watches a file of the node",
        ["coded"] = "compiles C++ at run time"
    };

    private const string CONTROL_DICT = "system/controlDict";

    private const string FUNCTIONS = "functions";

    private static readonly Regex TYPE = new(@"(?<![A-Za-z0-9_])type\s+([A-Za-z0-9_]+)\s*;", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A quoted string, or a bare word that is a path: it has a slash, starts a drive, or expands a variable.</summary>
    private static readonly Regex PATH_LIKE = new(@"""([^""\n]*)""|(?<![A-Za-z0-9_""./$-])((?:\$\{?[A-Za-z_][A-Za-z0-9_]*\}?|~|[A-Za-z]:|\.\.?)?[\\/][^\s;()""]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    #endregion

    #region Functions

    /// <summary>
    /// Inspects the function objects a controlDict carries: every block of
    /// its <c>functions</c> dictionary must be of an allowed type, and no
    /// entry of a block may name a path outside the case.
    /// </summary>
    /// <param name="controlDictText">The controlDict's text, comments already blanked.</param>
    /// <param name="relativePath">The file's path in the case, for the sentences.</param>
    /// <param name="findings">Receives one sentence per refusal, with file and line.</param>
    public static void Inspect(string controlDictText, string relativePath, List<string> findings)
    {
        var functions = FindFunctions(controlDictText);
        if (functions == null)
            return;

        foreach (var (name, body, bodyStart) in Blocks(controlDictText, functions.Value.Start, functions.Value.End))
        {
            var line = LineOf(controlDictText, bodyStart);
            var type = TYPE.Match(body);
            if (!type.Success)
            {
                findings.Add($"{relativePath}:{line}: the function object '{name}' names no type.");
                continue;
            }

            var typeName = type.Groups[1].Value;
            if (!ALLOWED.Contains(typeName, StringComparer.Ordinal))
            {
                var why = REFUSED_BECAUSE.TryGetValue(typeName, out var reason) ? reason : "is not among the function objects a pool node runs";
                findings.Add($"{relativePath}:{line}: the function object '{name}' is of type '{typeName}', which {why}; a case may carry only measurements and writers that stay inside it.");
                continue;
            }

            foreach (var path in PathsIn(body))
            {
                if (!FoamCaseContentRules.StaysInsideTheCase(path, relativePath))
                    findings.Add($"{relativePath}:{line}: the function object '{name}' names \"{path}\", which reaches outside the case.");
            }
        }
    }

    /// <summary>
    /// Whether a function object type is one a case may carry.
    /// </summary>
    /// <param name="type">The type word.</param>
    /// <returns>True when the type is allowed.</returns>
    public static bool IsAllowed(string type)
    {
        return ALLOWED.Contains(type, StringComparer.Ordinal);
    }

    /// <summary>
    /// Whether a file is the one the function objects live in.
    /// </summary>
    /// <param name="relativePath">The file's relative path, forward slashes.</param>
    /// <returns>True for the case's controlDict.</returns>
    public static bool Carries(string relativePath)
    {
        return relativePath == CONTROL_DICT;
    }

    // The span of the functions dictionary's body (between its braces), or
    // null when the controlDict has none or it is not a block (a macro, an
    // #include alone - the solver refuses those itself).
    private static (int Start, int End)? FindFunctions(string text)
    {
        var index = -1;
        while ((index = text.IndexOf(FUNCTIONS, index + 1, StringComparison.Ordinal)) >= 0)
        {
            if (index > 0 && IsWord(text[index - 1]))
                continue;

            var after = index + FUNCTIONS.Length;
            if (after < text.Length && IsWord(text[after]))
                continue;

            var brace = SkipBlanks(text, after);
            if (brace >= text.Length || text[brace] != '{')
                continue;

            var close = MatchingBrace(text, brace);
            return close < 0 ? null : (brace + 1, close);
        }

        return null;
    }

    // The top-level blocks of a dictionary body: "name { ... }" each, the
    // name a word before the brace; entries that are not blocks are skipped.
    private static IEnumerable<(string Name, string Body, int BodyStart)> Blocks(string text, int start, int end)
    {
        var position = start;
        while (position < end)
        {
            var brace = text.IndexOf('{', position, end - position);
            if (brace < 0)
                yield break;

            var close = MatchingBrace(text, brace);
            if (close < 0 || close > end)
                yield break;

            var name = WordBefore(text, brace, start);
            yield return (name, text[(brace + 1)..close], brace);
            position = close + 1;
        }
    }

    private static IEnumerable<string> PathsIn(string body)
    {
        foreach (Match match in PATH_LIKE.Matches(body))
        {
            var value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            if (value.Length > 0 && (value.Contains('/') || value.Contains('\\') || value.StartsWith('$') || value.StartsWith('~')))
                yield return value;
        }
    }

    private static int MatchingBrace(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{')
                depth++;
            else if (text[i] == '}' && --depth == 0)
                return i;
        }

        return -1;
    }

    private static string WordBefore(string text, int brace, int floor)
    {
        var end = brace;
        while (end > floor && char.IsWhiteSpace(text[end - 1]))
            end--;

        var begin = end;
        while (begin > floor && IsWord(text[begin - 1]))
            begin--;

        return text[begin..end];
    }

    private static int SkipBlanks(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;

        return index;
    }

    private static bool IsWord(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    private static int LineOf(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
                line++;
        }

        return line;
    }

    #endregion
}
