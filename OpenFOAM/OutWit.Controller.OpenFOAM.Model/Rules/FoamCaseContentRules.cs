using System.Text.RegularExpressions;

namespace OutWit.Controller.OpenFOAM.Model.Rules;

/// <summary>
/// The rejects that need a case's contents: run-time code (<c>codeStream</c>,
/// <c>#codeStream</c>, <c>coded*</c> conditions and function objects,
/// <c>#calc</c>, a <c>dynamicCode/</c> directory), libraries outside the kit,
/// includes outside the case, a decomposed-only case, a missing application.
/// The kit ships no compiler, so run-time code would fail hard anyway; the
/// reject is the honest message before that, with file and line. The node
/// applies the rules to the materialised case before anything runs and the
/// initiator to the case the user opened - the same code, the same sentences.
/// Files are given as their relative paths and their text in the
/// <see cref="FoamCaseText"/> view; which files need their text read is
/// <see cref="IsScanned"/>'s answer.
/// </summary>
public static class FoamCaseContentRules
{
    #region Constants

    /// <summary>Files larger than this are not scanned: no dictionary is that large, fields and meshes are.</summary>
    public const long MAX_SCANNED_BYTES = 8 * 1024 * 1024;

    private const string CONTROL_DICT = "system/controlDict";

    private const string DYNAMIC_CODE = "dynamicCode";

    private const string POST_PROCESSING = "postProcessing";

    private const string PROCESSOR_PREFIX = "processor";

    private const string POLY_MESH_SEGMENT = "/polyMesh/";

    private static readonly Regex CODE_STREAM = new(@"(?<![A-Za-z0-9_])#?codeStream(?![A-Za-z0-9_])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CALC = new(@"(?<![A-Za-z0-9_])#calc(?![A-Za-z0-9_])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CODED = new(@"(?<![A-Za-z0-9_])coded[A-Z][A-Za-z0-9_]*(?![A-Za-z0-9_])|(?<![A-Za-z0-9_])type\s+coded\s*;", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LIBS = new(@"(?<![A-Za-z0-9_])libs\s*\(([^)]*)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Every include directive OpenFOAM knows, quoted target captured: #include, #sinclude, #includeIfPresent, #includeFunc, #includeEtc.</summary>
    private static readonly Regex INCLUDE = new(@"#s?include(?:IfPresent|Func|Etc)?\s+""([^""]+)""", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The case-root prefixes OpenFOAM expands: the rest of the path must still stay inside the case.</summary>
    private static readonly string[] CASE_PREFIXES = ["$FOAM_CASE/", "${FOAM_CASE}/", "<case>/"];

    private static readonly Regex APPLICATION = new(@"(?m)^\s*application\s+([A-Za-z0-9_]+)\s*;", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LINE_COMMENT = new(@"//[^\n]*", RegexOptions.Compiled);

    private static readonly Regex BLOCK_COMMENT = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

    #endregion

    #region Functions

    /// <summary>
    /// Inspects a case.
    /// </summary>
    /// <param name="files">Every file of the case: its relative path (forward slashes) and its text in the <see cref="FoamCaseText"/> view, or null when it was not read (<see cref="IsScanned"/> said no, or it could not be read).</param>
    /// <param name="kitHasLibrary">Answers whether a library named in a <c>libs</c> entry is in the kit; null accepts every name.</param>
    /// <returns>Findings, one sentence each with file and line where known; empty when the case may run.</returns>
    public static IReadOnlyList<string> Inspect(IReadOnlyList<(string RelativePath, string? Text)> files, Func<string, bool>? kitHasLibrary = null)
    {
        var findings = new List<string>();

        if (files.Any(file => FirstSegment(file.RelativePath) == DYNAMIC_CODE))
            findings.Add("dynamicCode/: the case carries compiled run-time code; the kit ships no compiler.");

        var controlDict = files.Where(file => file.RelativePath == CONTROL_DICT).ToList();
        if (controlDict.Count == 0)
            findings.Add("system/controlDict: missing.");
        else if (controlDict[0].Text == null)
            findings.Add("system/controlDict: could not be read.");
        else if (ReadApplication(controlDict[0].Text!) == null)
            findings.Add("system/controlDict: no 'application' entry.");

        if (IsDecomposedOnly(files))
            findings.Add("processor*/: the case is decomposed only; a reconstructed case is required.");

        // A binary field is scanned like any other file: its entries around the
        // binary list are dictionary text (a coded boundary condition among
        // them), and the one-byte view reads any byte without harm.
        foreach (var (relativePath, text) in files)
        {
            if (text == null || !IsScannedPath(relativePath))
                continue;

            InspectText(relativePath, StripComments(text), kitHasLibrary, findings);
        }

        return findings;
    }

    /// <summary>
    /// Whether a file's text takes part in the inspection: a file outside a
    /// mesh, a decomposed copy, an earlier run's output and compiled code, and
    /// no larger than <see cref="MAX_SCANNED_BYTES"/>. A caller reads the text
    /// of exactly these files.
    /// </summary>
    /// <param name="relativePath">The file's relative path, forward slashes.</param>
    /// <param name="size">The file's size in bytes.</param>
    /// <returns>True when the text should be read and given to <see cref="Inspect"/>.</returns>
    public static bool IsScanned(string relativePath, long size)
    {
        return size <= MAX_SCANNED_BYTES && IsScannedPath(relativePath);
    }

    /// <summary>
    /// The application named in a controlDict.
    /// </summary>
    /// <param name="controlDictText">The controlDict's text.</param>
    /// <returns>The solver name, or null when there is no uncommented entry.</returns>
    public static string? ReadApplication(string controlDictText)
    {
        var match = APPLICATION.Match(StripComments(controlDictText));
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Whether an include target resolves inside the case. A case-root prefix
    /// ($FOAM_CASE/, ${FOAM_CASE}/, &lt;case&gt;/) is stripped and the rest judged
    /// like any relative path, so "$FOAM_CASE/../x" is refused; an #includeEtc
    /// target is a path under the kit's etc/ and is judged the same way; any
    /// other variable or a home-relative path is refused - what it expands to
    /// on a node is not the user's to decide.
    /// </summary>
    /// <param name="target">The quoted include target.</param>
    /// <returns>True when the include cannot leave the case (or the kit's etc/ for #includeEtc).</returns>
    public static bool StaysInsideTheCase(string target)
    {
        var path = target;
        foreach (var prefix in CASE_PREFIXES)
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal))
            {
                path = path[prefix.Length..];
                break;
            }
        }

        if (path.Length == 0 || path.StartsWith('$') || path.StartsWith('~') || path.StartsWith('<'))
            return false;

        return !FoamCasePathRules.IsPathEscape(path);
    }

    private static void InspectText(string relativePath, string text, Func<string, bool>? kitHasLibrary, List<string> findings)
    {
        Report(findings, relativePath, text, CODE_STREAM, "codeStream compiles C++ at run time");
        Report(findings, relativePath, text, CALC, "#calc compiles C++ at run time (#eval is the in-built evaluator and is allowed)");
        Report(findings, relativePath, text, CODED, "a coded condition or function object compiles C++ at run time");

        foreach (Match match in LIBS.Matches(text))
        {
            foreach (var library in match.Groups[1].Value.Split([' ', '\t', '\r', '\n', '"'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (kitHasLibrary != null && !kitHasLibrary(library))
                    findings.Add($"{relativePath}:{LineOf(text, match.Index)}: libs names '{library}', which is not in the kit.");
            }
        }

        foreach (Match match in INCLUDE.Matches(text))
        {
            var target = match.Groups[1].Value;
            if (!StaysInsideTheCase(target))
                findings.Add($"{relativePath}:{LineOf(text, match.Index)}: {match.Value.Split(' ', '\t')[0]} \"{target}\" reaches outside the case.");
        }
    }

    private static bool IsDecomposedOnly(IReadOnlyList<(string RelativePath, string? Text)> files)
    {
        var processors = files.Any(file => FirstSegment(file.RelativePath).StartsWith(PROCESSOR_PREFIX, StringComparison.Ordinal) && file.RelativePath.Contains('/'));
        var hasMesh = files.Any(file => file.RelativePath.StartsWith("constant/polyMesh/", StringComparison.Ordinal));
        var hasTimeZero = files.Any(file => FirstSegment(file.RelativePath) is "0" or "0.orig" && file.RelativePath.Contains('/'));
        return processors && !hasMesh && !hasTimeZero;
    }

    private static bool IsScannedPath(string relativePath)
    {
        if (relativePath.Contains('/'))
        {
            var first = FirstSegment(relativePath);
            if (first.StartsWith(PROCESSOR_PREFIX, StringComparison.Ordinal) || first == POST_PROCESSING || first == DYNAMIC_CODE)
                return false;
        }

        return !relativePath.Contains(POLY_MESH_SEGMENT, StringComparison.Ordinal);
    }

    private static string FirstSegment(string relativePath)
    {
        var slash = relativePath.IndexOf('/');
        return slash < 0 ? relativePath : relativePath[..slash];
    }

    private static void Report(List<string> findings, string relativePath, string text, Regex pattern, string why)
    {
        var match = pattern.Match(text);
        if (match.Success)
            findings.Add($"{relativePath}:{LineOf(text, match.Index)}: {why}.");
    }

    private static string StripComments(string text)
    {
        // Comments are blanked, not removed, so line numbers survive.
        text = BLOCK_COMMENT.Replace(text, match => new string(match.Value.Select(c => c == '\n' ? '\n' : ' ').ToArray()));
        return LINE_COMMENT.Replace(text, match => new string(' ', match.Length));
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
