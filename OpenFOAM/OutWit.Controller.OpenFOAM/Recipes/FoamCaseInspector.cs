using System.Text;
using System.Text.RegularExpressions;

namespace OutWit.Controller.OpenFOAM.Recipes;

/// <summary>
/// The node-side rejects of plan D-14, applied to the materialised case
/// before anything runs: run-time code (<c>codeStream</c>, <c>#codeStream</c>,
/// <c>coded*</c> conditions and function objects, <c>#calc</c>, a
/// <c>dynamicCode/</c> directory), libraries outside the kit, includes
/// outside the case, a decomposed-only case, a missing application. The kit
/// ships no compiler, so run-time code would fail hard anyway; the reject is
/// the honest message before that, with file and line.
/// </summary>
public static class FoamCaseInspector
{
    #region Constants

    private const long MAX_SCANNED_BYTES = 8 * 1024 * 1024;

    private static readonly Regex CODE_STREAM = new(@"(?<![A-Za-z0-9_])#?codeStream(?![A-Za-z0-9_])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CALC = new(@"(?<![A-Za-z0-9_])#calc(?![A-Za-z0-9_])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CODED = new(@"(?<![A-Za-z0-9_])coded[A-Z][A-Za-z0-9_]*(?![A-Za-z0-9_])|(?<![A-Za-z0-9_])type\s+coded\s*;", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LIBS = new(@"(?<![A-Za-z0-9_])libs\s*\(([^)]*)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex INCLUDE = new(@"#include(?:IfPresent|Func)?\s+""([^""]+)""", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex APPLICATION = new(@"(?m)^\s*application\s+([A-Za-z0-9_]+)\s*;", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LINE_COMMENT = new(@"//[^\n]*", RegexOptions.Compiled);

    private static readonly Regex BLOCK_COMMENT = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

    #endregion

    #region Functions

    /// <summary>
    /// Inspects a case directory.
    /// </summary>
    /// <param name="caseDirectory">The case root.</param>
    /// <param name="kitHasLibrary">Answers whether a library named in a <c>libs</c> entry is in the kit; null accepts every name.</param>
    /// <returns>Findings, one sentence each with file and line where known; empty when the case may run.</returns>
    public static IReadOnlyList<string> Inspect(string caseDirectory, Func<string, bool>? kitHasLibrary = null)
    {
        var findings = new List<string>();
        var root = Path.GetFullPath(caseDirectory);

        if (Directory.Exists(Path.Combine(root, "dynamicCode")))
            findings.Add("dynamicCode/: the case carries compiled run-time code; the kit ships no compiler.");

        var controlDict = Path.Combine(root, "system", "controlDict");
        if (!File.Exists(controlDict))
            findings.Add("system/controlDict: missing.");
        else if (!APPLICATION.IsMatch(StripComments(File.ReadAllText(controlDict))))
            findings.Add("system/controlDict: no 'application' entry.");

        var processors = Directory.EnumerateDirectories(root, "processor*").Any();
        var hasMesh = Directory.Exists(Path.Combine(root, "constant", "polyMesh"));
        var hasTimeZero = Directory.Exists(Path.Combine(root, "0")) || Directory.Exists(Path.Combine(root, "0.orig"));
        if (processors && !hasMesh && !hasTimeZero)
            findings.Add("processor*/: the case is decomposed only; a reconstructed case is required.");

        foreach (var file in ScannableFiles(root))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            string text;
            try
            {
                text = File.ReadAllText(file, Encoding.UTF8);
            }
            catch (IOException)
            {
                continue;
            }

            if (LooksBinary(text))
                continue;

            var stripped = StripComments(text);
            Report(findings, relative, stripped, CODE_STREAM, "codeStream compiles C++ at run time");
            Report(findings, relative, stripped, CALC, "#calc compiles C++ at run time (#eval is the in-built evaluator and is allowed)");
            Report(findings, relative, stripped, CODED, "a coded condition or function object compiles C++ at run time");

            foreach (Match match in LIBS.Matches(stripped))
            {
                foreach (var library in match.Groups[1].Value.Split([' ', '\t', '\r', '\n', '"'], StringSplitOptions.RemoveEmptyEntries))
                {
                    if (kitHasLibrary != null && !kitHasLibrary(library))
                        findings.Add($"{relative}:{LineOf(stripped, match.Index)}: libs names '{library}', which is not in the kit.");
                }
            }

            foreach (Match match in INCLUDE.Matches(stripped))
            {
                var target = match.Groups[1].Value;
                if (target.StartsWith("$FOAM_CASE", StringComparison.Ordinal) || target.StartsWith("<case>", StringComparison.Ordinal))
                    continue;
                if (FoamRecipeValidator.IsPathEscape(target) || target.StartsWith('$') || target.StartsWith('~'))
                    findings.Add($"{relative}:{LineOf(stripped, match.Index)}: #include \"{target}\" reaches outside the case.");
            }
        }

        return findings;
    }

    /// <summary>
    /// The application named in the case's controlDict.
    /// </summary>
    /// <param name="caseDirectory">The case root.</param>
    /// <returns>The solver name, or null when the entry is missing.</returns>
    public static string? ReadApplication(string caseDirectory)
    {
        var controlDict = Path.Combine(caseDirectory, "system", "controlDict");
        if (!File.Exists(controlDict))
            return null;

        var match = APPLICATION.Match(StripComments(File.ReadAllText(controlDict)));
        return match.Success ? match.Groups[1].Value : null;
    }

    private static IEnumerable<string> ScannableFiles(string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith("processor", StringComparison.Ordinal) || name == "postProcessing" || name == "dynamicCode")
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (relative.Contains("/polyMesh/", StringComparison.Ordinal))
                    continue;
                if (new FileInfo(file).Length > MAX_SCANNED_BYTES)
                    continue;

                yield return file;
            }
        }

        foreach (var file in Directory.EnumerateFiles(root))
        {
            if (new FileInfo(file).Length <= MAX_SCANNED_BYTES)
                yield return file;
        }
    }

    private static void Report(List<string> findings, string relative, string text, Regex pattern, string why)
    {
        var match = pattern.Match(text);
        if (match.Success)
            findings.Add($"{relative}:{LineOf(text, match.Index)}: {why}.");
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

    private static bool LooksBinary(string text)
    {
        var probe = Math.Min(text.Length, 4096);
        for (var i = 0; i < probe; i++)
        {
            var c = text[i];
            if (c == '\0' || c == '�')
                return true;
        }

        return false;
    }

    #endregion
}
