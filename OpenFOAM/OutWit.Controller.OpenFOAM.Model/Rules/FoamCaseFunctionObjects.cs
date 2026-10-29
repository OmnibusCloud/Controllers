using System.Text.RegularExpressions;

namespace OutWit.Controller.OpenFOAM.Model.Rules;

/// <summary>
/// The function objects a case runs, read from the text of its
/// <c>system/controlDict</c> the way OpenFOAM v2606 reads them: the blocks of
/// the last top-level <c>functions</c> dictionary, the blocks of every case
/// file that dictionary <c>#include</c>s, and every <c>#includeFunc</c> line -
/// resolved to the case's own <c>system/&lt;name&gt;</c> when the case ships
/// one (<c>functionObjectList::findDict</c> looks there first) and to the
/// kit's <c>etc/caseDicts/postProcessing</c> otherwise. One enumeration for
/// the rules (every function object against the allow-list, wherever it is
/// written), for the node (a response the case already runs itself) and for
/// an initiator (what a case offers to measure). Comments are blanked
/// before reading, strings and <c>#{ #}</c> blocks are never read into.
/// </summary>
public static class FoamCaseFunctionObjects
{
    #region Constants

    private const string FUNCTIONS = "functions";

    private const string CONTROL_DICT = "system/controlDict";

    private const string SYSTEM_PREFIX = "system/";

    private const string TYPE = "type";

    /// <summary>The kit's <c>etc/caseDicts/postProcessing</c>, as a path for the sentences.</summary>
    public const string KIT_DICTIONARIES = "etc/caseDicts/postProcessing";

    /// <summary>An <c>#includeFunc</c> by name, with its optional arguments in parentheses (<c>probes(p, U)</c>).</summary>
    private static readonly Regex INCLUDE_FUNC = new(@"#includeFunc\s+([A-Za-z_][A-Za-z0-9_]*)(?:\(([^)\n]*)\))?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>An include of a file by quoted path: #include, #sinclude, #includeIfPresent.</summary>
    private static readonly Regex INCLUDE_FILE = new(@"#(?:s?include|includeIfPresent)\s+""([^""\n]+)""", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>An include of a kit cfg under caseDicts/postProcessing, which carries the function object's type (pitzDaily's streamlines).</summary>
    private static readonly Regex INCLUDE_ETC = new(@"#includeEtc\s+""caseDicts/postProcessing/(?:[A-Za-z0-9_]+/)*([A-Za-z0-9_]+)(?:\.cfg)?""", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LINE_COMMENT = new(@"//[^\n]*", RegexOptions.Compiled);

    private static readonly Regex BLOCK_COMMENT = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// The kit's dictionaries whose type is not their name (measured on the
    /// v2606 kit, 2026-10-28; every other one named after an allowed type is
    /// that type). A name outside this table and the allow-list is a kit
    /// dictionary of a type the Model does not know.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> KIT_TYPES = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["forcesIncompressible"] = "forces",
        ["forcesCompressible"] = "forces",
        ["forceCoeffsIncompressible"] = "forceCoeffs",
        ["forceCoeffsCompressible"] = "forceCoeffs",
        ["cellMax"] = "volFieldValue",
        ["cellMin"] = "volFieldValue",
        ["faceMax"] = "surfaceFieldValue",
        ["faceMin"] = "surfaceFieldValue",
        ["minMaxMagnitude"] = "fieldMinMax",
        ["minMaxComponents"] = "fieldMinMax",
        ["patchAverage"] = "surfaceFieldValue",
        ["patchIntegrate"] = "surfaceFieldValue",
        ["flowRatePatch"] = "surfaceFieldValue",
        ["flowRateFaceZone"] = "surfaceFieldValue",
        ["volFlowRateSurface"] = "surfaceFieldValue",
        ["pressureDifferencePatch"] = "multiFieldValue",
        ["pressureDifferenceSurface"] = "multiFieldValue",
        ["R"] = "turbulenceFields",
        ["staticPressure"] = "pressure",
        ["totalPressureIncompressible"] = "pressure",
        ["totalPressureCompressible"] = "pressure",
        ["singleGraph"] = "sets",
        ["internalCloud"] = "sets",
        ["boundaryCloud"] = "sets",
        ["streamlines"] = "streamLine"
    };

    private static readonly (string Prefix, string Folder)[] CASE_PREFIXES =
    [
        ("$FOAM_CASE/", ""),
        ("${FOAM_CASE}/", ""),
        ("<case>/", ""),
        ("<system>/", "system/"),
        ("<constant>/", "constant/")
    ];

    #endregion

    #region Functions

    /// <summary>
    /// The last top-level <c>functions</c> entry of a controlDict: its line
    /// and whether it is a block - <c>functions #includeEtc "..."</c> or
    /// <c>functions $macro;</c> is not, and what such an entry pulls in cannot
    /// be read here nor added to by the controller.
    /// </summary>
    /// <param name="controlDictText">The controlDict's text.</param>
    /// <returns>The line and the verdict; null when the file has no top-level <c>functions</c>.</returns>
    public static (int Line, bool IsBlock)? Functions(string controlDictText)
    {
        var span = FindFunctions(StripComments(controlDictText));
        return span == null ? null : (span.Value.Line, span.Value.IsBlock);
    }

    /// <summary>
    /// Enumerates the function objects a controlDict runs.
    /// </summary>
    /// <param name="controlDictText">The controlDict's text.</param>
    /// <param name="readCaseFile">Reads a case file by its relative path (forward slashes), null when the case has no such file; null reads no file, so an <c>#include</c>d file yields nothing and every <c>#includeFunc</c> counts as the kit's.</param>
    /// <returns>The function objects in the order the file runs them; empty when there is no <c>functions</c> block.</returns>
    public static IReadOnlyList<FoamCaseFunctionObject> Enumerate(string controlDictText, Func<string, string?>? readCaseFile = null)
    {
        var text = StripComments(controlDictText);
        var span = FindFunctions(text);
        if (span == null || !span.Value.IsBlock)
            return [];

        var start = span.Value.Start;
        var end = span.Value.End;
        var found = new List<(int Index, FoamCaseFunctionObject Item)>();

        // The blocks first, then the include lines with the blocks' bodies
        // blanked so that nothing inside a block is taken for an include.
        var blanked = text.ToCharArray();
        foreach (var (name, body, bodyStart) in Blocks(text, start, end))
        {
            var type = TypeOf(body, out var typedByKit);
            found.Add((bodyStart, new FoamCaseFunctionObject(name, type, body, CONTROL_DICT, LineOf(text, bodyStart), FoamCaseFunctionObjectSource.Block, typedByKit: typedByKit)));
            for (var index = bodyStart; index < bodyStart + body.Length + 2 && index < blanked.Length; index++)
                blanked[index] = blanked[index] == '\n' ? '\n' : ' ';
        }

        var between = new string(blanked, start, end - start);

        foreach (Match match in INCLUDE_FUNC.Matches(between))
        {
            var name = match.Groups[1].Value;
            var arguments = match.Groups[2].Success ? match.Groups[2].Value.Trim() : string.Empty;
            var line = LineOf(text, start + match.Index);
            var own = readCaseFile?.Invoke(SYSTEM_PREFIX + name);
            if (own == null)
            {
                found.Add((start + match.Index, new FoamCaseFunctionObject(name, KitType(name), string.Empty, KIT_DICTIONARIES + "/" + name, line, FoamCaseFunctionObjectSource.IncludedKitDictionary, arguments, typedByKit: true)));
                continue;
            }

            // The case's dictionary: its entries at the top level, or wrapped
            // in a block named after the function (v2606 reads either).
            var ownText = StripComments(own);
            var wrapped = Blocks(ownText, 0, ownText.Length).FirstOrDefault(block => block.Name == name);
            var ownBody = wrapped.Body ?? ownText;
            var ownType = TypeOf(ownBody, out var ownTypedByKit);
            found.Add((start + match.Index, new FoamCaseFunctionObject(name, ownType, ownBody, SYSTEM_PREFIX + name, line, FoamCaseFunctionObjectSource.IncludedCaseDictionary, arguments, ownTypedByKit)));
        }

        foreach (Match match in INCLUDE_FILE.Matches(between))
        {
            var path = ResolveCasePath(match.Groups[1].Value, CONTROL_DICT);
            var included = path == null ? null : readCaseFile?.Invoke(path);
            if (included == null)
                continue;

            var includedText = StripComments(included);
            foreach (var (name, body, bodyStart) in Blocks(includedText, 0, includedText.Length))
            {
                var includedType = TypeOf(body, out var includedTypedByKit);
                found.Add((start + match.Index, new FoamCaseFunctionObject(name, includedType, body, path!, LineOf(includedText, bodyStart), FoamCaseFunctionObjectSource.IncludedFile, typedByKit: includedTypedByKit)));
            }
        }

        // In file order, blocks and includes alike (a stable sort keeps an
        // included file's blocks in their own order).
        return found.OrderBy(entry => entry.Index).Select(entry => entry.Item).ToList();
    }

    /// <summary>
    /// The value text of a top-level entry of a dictionary body, as written
    /// and without its semicolon: <c>(0 0 0)</c>, <c>rhoInf</c>, a
    /// sub-dictionary with its braces. A <c>$</c> reference stays a reference.
    /// </summary>
    /// <param name="body">The dictionary text between its braces.</param>
    /// <param name="keyword">The entry's keyword.</param>
    /// <returns>The value, or null when the body has no such entry at its top level.</returns>
    public static string? Entry(string body, string keyword)
    {
        var depth = 0;
        var index = 0;
        while (index < body.Length)
        {
            var current = body[index];
            if (current == '"')
            {
                index = SkipString(body, index + 1);
                continue;
            }

            if (current == '{')
            {
                depth++;
                index++;
                continue;
            }

            if (current == '}')
            {
                depth--;
                index++;
                continue;
            }

            if (depth == 0 && IsWordStart(body, index) && string.CompareOrdinal(body, index, keyword, 0, keyword.Length) == 0
                && index + keyword.Length < body.Length && char.IsWhiteSpace(body[index + keyword.Length]))
            {
                var valueStart = SkipBlanks(body, index + keyword.Length);
                var valueEnd = EndOfValue(body, valueStart);
                return body[valueStart..valueEnd].Trim();
            }

            index++;
        }

        return null;
    }

    /// <summary>
    /// The case files a dictionary text <c>#include</c>s (<c>#include</c>,
    /// <c>#sinclude</c>, <c>#includeIfPresent</c>), each resolved the way
    /// OpenFOAM resolves it from the including file, in the order they are
    /// written and each once; a target outside the case is left out.
    /// Comments are blanked first.
    /// </summary>
    /// <param name="text">The including file's text.</param>
    /// <param name="includingFile">The including file's relative path.</param>
    /// <returns>The relative paths, forward slashes.</returns>
    public static IReadOnlyList<string> IncludedFiles(string text, string includingFile)
    {
        var files = new List<string>();
        foreach (Match match in INCLUDE_FILE.Matches(StripComments(text)))
        {
            var path = ResolveCasePath(match.Groups[1].Value, includingFile);
            if (path != null && !files.Contains(path, StringComparer.Ordinal))
                files.Add(path);
        }

        return files;
    }

    /// <summary>
    /// A case file's path as OpenFOAM resolves an include target from the
    /// including file: a case-root prefix from the root, a relative path
    /// beside the including file, <c>..</c> walked; null when the target is
    /// not a path inside the case.
    /// </summary>
    /// <param name="target">The quoted include target.</param>
    /// <param name="includingFile">The including file's relative path.</param>
    /// <returns>The relative path, forward slashes, or null.</returns>
    public static string? ResolveCasePath(string target, string includingFile)
    {
        if (!FoamCaseContentRules.StaysInsideTheCase(target, includingFile))
            return null;

        var path = target;
        var folder = FolderOf(includingFile);
        foreach (var (prefix, prefixFolder) in CASE_PREFIXES)
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal))
            {
                path = path[prefix.Length..];
                folder = prefixFolder;
                break;
            }
        }

        var parts = new List<string>();
        foreach (var part in (folder + path).Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "..")
                parts.RemoveAt(parts.Count - 1);
            else if (part != ".")
                parts.Add(part);
        }

        return string.Join('/', parts);
    }

    #endregion

    #region Tools

    /// <summary>The last top-level <c>functions</c>: the span of its body when it is a block.</summary>
    private static (int Start, int End, int Line, bool IsBlock)? FindFunctions(string text)
    {
        (int Start, int End, int Line, bool IsBlock)? last = null;
        var depth = 0;
        var index = 0;
        while (index < text.Length)
        {
            var current = text[index];
            if (current == '"')
            {
                index = SkipString(text, index + 1);
                continue;
            }

            if (current == '#' && index + 1 < text.Length && text[index + 1] == '{')
            {
                var close = text.IndexOf("#}", index + 2, StringComparison.Ordinal);
                index = close < 0 ? text.Length : close + 2;
                continue;
            }

            if (current == '{')
            {
                depth++;
                index++;
                continue;
            }

            if (current == '}')
            {
                depth = Math.Max(0, depth - 1);
                index++;
                continue;
            }

            if (depth == 0 && IsWordStart(text, index) && string.CompareOrdinal(text, index, FUNCTIONS, 0, FUNCTIONS.Length) == 0
                && (index + FUNCTIONS.Length >= text.Length || !IsWord(text[index + FUNCTIONS.Length])))
            {
                var line = LineOf(text, index);
                var brace = SkipBlanks(text, index + FUNCTIONS.Length);
                if (brace < text.Length && text[brace] == '{')
                {
                    var close = MatchingBrace(text, brace);
                    if (close < 0)
                        return (brace + 1, text.Length, line, false);

                    last = (brace + 1, close, line, true);
                    index = close + 1;
                    continue;
                }

                last = (index, index, line, false);
                index += FUNCTIONS.Length;
                continue;
            }

            index++;
        }

        return last;
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

    // The type a body declares; without one, the type of the last kit cfg
    // it includes (the kit's, known to the Model or not).
    private static string? TypeOf(string body, out bool typedByKit)
    {
        typedByKit = false;
        var type = Entry(body, TYPE);
        if (type != null && Regex.IsMatch(type, "^[A-Za-z0-9_]+$"))
            return type;

        var cfg = INCLUDE_ETC.Matches(body).LastOrDefault();
        if (cfg == null)
            return null;

        typedByKit = true;
        return KitType(cfg.Groups[1].Value);
    }

    private static string? KitType(string name)
    {
        if (KIT_TYPES.TryGetValue(name, out var type))
            return type;

        return FoamFunctionObjectRules.IsAllowed(name) ? name : null;
    }

    private static int EndOfValue(string text, int start)
    {
        var depth = 0;
        var index = start;
        while (index < text.Length)
        {
            var current = text[index];
            if (current == '"')
            {
                index = SkipString(text, index + 1);
                continue;
            }

            if (current == '{')
                depth++;
            else if (current == '}')
            {
                depth--;
                if (depth == 0)
                    return index + 1;
                if (depth < 0)
                    return index;
            }
            else if (current == ';' && depth == 0)
                return index;

            index++;
        }

        return text.Length;
    }

    private static int MatchingBrace(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                i = SkipString(text, i + 1) - 1;
                continue;
            }

            if (text[i] == '{')
                depth++;
            else if (text[i] == '}' && --depth == 0)
                return i;
        }

        return -1;
    }

    private static int SkipString(string text, int index)
    {
        while (index < text.Length && text[index] != '"')
            index += text[index] == '\\' ? 2 : 1;

        return Math.Min(text.Length, index + 1);
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

    private static bool IsWordStart(string text, int index)
    {
        return index == 0 || !IsWord(text[index - 1]);
    }

    private static bool IsWord(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    private static string FolderOf(string relativePath)
    {
        var slash = relativePath.LastIndexOf('/');
        return slash < 0 ? string.Empty : relativePath[..(slash + 1)];
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
