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
/// case. Every function object the case runs is judged, wherever it is
/// written (<see cref="FoamCaseFunctionObjects"/>): a block of
/// <c>functions</c>, a block of a file it <c>#include</c>s, a dictionary of
/// the case's <c>system/</c> an <c>#includeFunc</c> pulls in; a dictionary
/// of the kit's own <c>etc/caseDicts/postProcessing</c> is the kit's and
/// passes. A <c>functions</c> entry that is not a block (<c>functions
/// #includeEtc "..."</c>) pulls in what cannot be read here and is refused
/// by line. The node applies the rule to the materialised case before
/// anything runs and the initiator to the case the user opened - the same
/// code, the same sentences.
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

    /// <summary>A quoted string, or a bare word that is a path: it has a slash, starts a drive, or expands a variable.</summary>
    private static readonly Regex PATH_LIKE = new(@"""([^""\n]*)""|(?<![A-Za-z0-9_""./$-])((?:\$\{?[A-Za-z_][A-Za-z0-9_]*\}?|~|[A-Za-z]:|\.\.?)?[\\/][^\s;()""]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    #endregion

    #region Functions

    /// <summary>
    /// Inspects the function objects a controlDict carries: every block of
    /// its <c>functions</c> dictionary must be of an allowed type, and no
    /// entry of a block may name a path outside the case. Without a file
    /// lookup, an <c>#include</c>d file is not read and every
    /// <c>#includeFunc</c> counts as the kit's.
    /// </summary>
    /// <param name="controlDictText">The controlDict's text, comments already blanked.</param>
    /// <param name="relativePath">The file's path in the case, for the sentences.</param>
    /// <param name="findings">Receives one sentence per refusal, with file and line.</param>
    public static void Inspect(string controlDictText, string relativePath, List<string> findings)
    {
        Inspect(controlDictText, relativePath, findings, null);
    }

    /// <summary>
    /// Inspects the function objects a controlDict carries, the ones it
    /// <c>#include</c>s and <c>#includeFunc</c>s from the case included.
    /// </summary>
    /// <param name="controlDictText">The controlDict's text.</param>
    /// <param name="relativePath">The file's path in the case, for the sentences.</param>
    /// <param name="findings">Receives one sentence per refusal, with file and line.</param>
    /// <param name="readCaseFile">Reads a case file by its relative path, null when there is none; null reads no file.</param>
    public static void Inspect(string controlDictText, string relativePath, List<string> findings, Func<string, string?>? readCaseFile)
    {
        var functions = FoamCaseFunctionObjects.Functions(controlDictText);
        if (functions == null)
            return;

        if (!functions.Value.IsBlock)
        {
            findings.Add($"{relativePath}:{functions.Value.Line}: 'functions' is not a block: what it pulls in cannot be read here, and the controller cannot add a response to it; write it as functions {{ ... }}.");
            return;
        }

        foreach (var function in FoamCaseFunctionObjects.Enumerate(controlDictText, readCaseFile))
        {
            var where = $"{(function.Source == FoamCaseFunctionObjectSource.IncludedCaseDictionary ? relativePath : function.RelativePath)}:{function.Line}";
            var name = function.Source == FoamCaseFunctionObjectSource.IncludedCaseDictionary ? $"{function.Name} (system/{function.Name})" : function.Name;

            // The kit's own dictionaries measure and write inside the case.
            if (function.Source == FoamCaseFunctionObjectSource.IncludedKitDictionary)
                continue;

            if (function.Type == null)
            {
                findings.Add($"{where}: the function object '{name}' names no type.");
                continue;
            }

            if (!IsAllowed(function.Type))
            {
                var why = REFUSED_BECAUSE.TryGetValue(function.Type, out var reason) ? reason : "is not among the function objects a pool node runs";
                findings.Add($"{where}: the function object '{name}' is of type '{function.Type}', which {why}; a case may carry only measurements and writers that stay inside it.");
                continue;
            }

            foreach (var path in PathsIn(function.Body))
            {
                if (!FoamCaseContentRules.StaysInsideTheCase(path, function.RelativePath))
                    findings.Add($"{where}: the function object '{name}' names \"{path}\", which reaches outside the case.");
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

    private static IEnumerable<string> PathsIn(string body)
    {
        foreach (Match match in PATH_LIKE.Matches(body))
        {
            var value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            if (value.Length > 0 && (value.Contains('/') || value.Contains('\\') || value.StartsWith('$') || value.StartsWith('~')))
                yield return value;
        }
    }

    #endregion
}
