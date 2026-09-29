using System.Diagnostics;
using System.Text;
using OutWit.Controller.OpenFOAM.Model.Rules;

namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// The controller's own <c>includeFunc &lt;response&gt;</c>: one line,
/// <c>#includeFunc &lt;response&gt;</c>, at the end of the top-level
/// <c>functions</c> block of the node's copy of <c>system/controlDict</c>, so
/// the solver measures the response while it solves - the response's
/// dictionary is <c>system/&lt;response&gt;</c>, which the controller wrote
/// before the first step, and <c>#includeFunc</c> reads the case's
/// <c>system/</c> first. A case without <c>functions</c> gets a block of its
/// own; one whose <c>functions</c> is not a block (<c>#includeEtc</c>, a
/// <c>$</c> reference) is refused by line, never rewritten. Only the last
/// top-level <c>functions</c> counts, as in OpenFOAM, and nothing in a
/// comment, a string, a <c>#{ #}</c> code block or a nested dictionary is
/// taken for it. The file's bytes are kept as they are, line endings
/// included; the case's own files, the ones the task carries, never change.
/// </summary>
public static class FoamSolveFunctions
{
    #region Constants

    private const string FUNCTIONS = "functions";

    private const string CONTROL_DICT = "system/controlDict";

    private const string INDENT = "    ";

    /// <summary>In place of a closing brace's index: the <c>functions</c> entry is not a block.</summary>
    private const int NOT_A_BLOCK = -1;

    /// <summary>In place of a closing brace's index: the <c>functions</c> block never closes.</summary>
    private const int NEVER_CLOSED = -2;

    /// <summary>What ends a word besides white space: OpenFOAM's punctuation and the start of a string.</summary>
    private const string DELIMITERS = "{}();\"";

    /// <summary>
    /// Latin-1 maps every byte to one character and back, so a file in any
    /// ASCII-compatible encoding is written back byte for byte, save the line
    /// the controller adds.
    /// </summary>
    private static readonly Encoding BYTES = Encoding.Latin1;

    #endregion

    #region Functions

    /// <summary>
    /// Adds the response to the solve in the node's copy of
    /// <c>system/controlDict</c>, and says in the log what was added, where
    /// and why.
    /// </summary>
    /// <param name="caseDirectory">The materialised case.</param>
    /// <param name="response">The response: a word, the name of its dictionary in <c>system/</c>.</param>
    /// <param name="logPath">The step's log, written whatever happens.</param>
    /// <returns>The step's outcome: 0 when the line is in; 1 with the reason in the log, the file left as it was, otherwise.</returns>
    public static FoamRunOutcome Include(string caseDirectory, string response, string logPath)
    {
        var stopwatch = Stopwatch.StartNew();
        var log = new StringBuilder();
        var exitCode = 0;

        try
        {
            exitCode = Include(caseDirectory, response, log);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.AppendLine($"--> {CONTROL_DICT} could not be edited: {exception.Message}");
            exitCode = 1;
        }

        var text = log.ToString();
        File.WriteAllText(logPath, text);
        return new FoamRunOutcome(exitCode, stopwatch.Elapsed.TotalSeconds, text);
    }

    /// <summary>
    /// Adds <c>#includeFunc &lt;response&gt;</c> to the text of a controlDict.
    /// </summary>
    /// <param name="text">The controlDict's text.</param>
    /// <param name="response">The response's name.</param>
    /// <returns>
    /// The edited text and the 1-based line of the added line; or a null text
    /// and the refusal, one sentence naming the line of the <c>functions</c>
    /// the controller cannot add to.
    /// </returns>
    public static (string? Text, int Line, string? Refusal) AddInclude(string text, string response)
    {
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var include = $"{INDENT}#{FoamAllowList.INCLUDE_FUNCTION} {response}";
        var functions = LastFunctions(text);

        if (functions == null)
            return Append(text, include, newLine);

        var (keywordLine, close, closeLine) = functions.Value;
        if (close == NOT_A_BLOCK)
            return (null, 0, $"{CONTROL_DICT}:{keywordLine}: '{FUNCTIONS}' is not a block the controller can add a line to; write it as {FUNCTIONS} {{ ... }} for a force to be measured during the solve.");

        if (close == NEVER_CLOSED)
            return (null, 0, $"{CONTROL_DICT}:{keywordLine}: '{FUNCTIONS}' opens a block that never closes.");

        var lineStart = close == 0 ? 0 : text.LastIndexOf('\n', close - 1) + 1;
        if (string.IsNullOrWhiteSpace(text[lineStart..close]))
            return (text[..lineStart] + include + newLine + text[lineStart..], closeLine, null);

        var end = close;
        while (end > lineStart && text[end - 1] is ' ' or '\t')
            end--;

        return (text[..end] + newLine + include + newLine + text[close..], closeLine + 1, null);
    }

    #endregion

    #region Tools

    private static int Include(string caseDirectory, string response, StringBuilder log)
    {
        var path = Path.Combine(caseDirectory, "system", "controlDict");
        var line = $"#{FoamAllowList.INCLUDE_FUNCTION} {response}";
        if (!File.Exists(path))
        {
            log.AppendLine($"--> the case has no {CONTROL_DICT} to add '{line}' to.");
            return 1;
        }

        var (edited, at, refusal) = AddInclude(File.ReadAllText(path, BYTES), response);
        if (edited == null)
        {
            log.AppendLine($"--> {refusal}");
            return 1;
        }

        File.WriteAllText(path, edited, BYTES);
        log.AppendLine($"Added '{line}' at {CONTROL_DICT}:{at}, in the functions of the node's copy of the case; the case's own files are not changed.");
        log.AppendLine($"The solver measures '{response}' while it solves, from system/{response}, which the controller wrote.");
        log.AppendLine("Why: a force measured after the solve (<solver> -postProcess) sees the walls a rotating zone (MRF) turns at rest, " +
                       "because OpenFOAM moves them only inside the solve; measured by the solver, the force is the solve's own.");
        return 0;
    }

    /// <summary>
    /// The last top-level <c>functions</c> entry: the line of its keyword, the
    /// index and line of its block's closing brace, or <see cref="NOT_A_BLOCK"/>
    /// / <see cref="NEVER_CLOSED"/> in place of the index; null when the text
    /// has none.
    /// </summary>
    private static (int KeywordLine, int Close, int CloseLine)? LastFunctions(string text)
    {
        (int KeywordLine, int Close, int CloseLine)? last = null;
        var depth = 0;
        var line = 1;
        var keywordLine = 0;
        var awaiting = false;
        var inBlock = false;

        var index = 0;
        while (index < text.Length)
        {
            var current = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';

            if (current == '\n')
            {
                line++;
                index++;
                continue;
            }

            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            if (current == '/' && next == '/')
            {
                index = SkipTo(text, index, "\n", ref line, consume: false);
                continue;
            }

            if (current == '/' && next == '*')
            {
                index = SkipTo(text, index + 2, "*/", ref line, consume: true);
                continue;
            }

            if (current == '{')
            {
                if (awaiting)
                {
                    awaiting = false;
                    inBlock = depth == 0;
                }

                depth++;
                index++;
                continue;
            }

            if (current == '}')
            {
                depth = Math.Max(0, depth - 1);
                if (inBlock && depth == 0)
                {
                    inBlock = false;
                    last = (keywordLine, index, line);
                }

                index++;
                continue;
            }

            // Anything else after the keyword makes it an entry that is not a block.
            if (awaiting)
            {
                awaiting = false;
                last = (keywordLine, NOT_A_BLOCK, keywordLine);
            }

            if (current == '#' && next == '{')
                index = SkipTo(text, index + 2, "#}", ref line, consume: true);
            else if (current == '"')
                index = SkipString(text, index + 1, ref line);
            else if (DELIMITERS.Contains(current))
                index++;
            else
            {
                var start = index;
                while (index < text.Length && !char.IsWhiteSpace(text[index]) && !DELIMITERS.Contains(text[index]) && !IsCommentAt(text, index))
                    index++;

                if (depth == 0 && index - start == FUNCTIONS.Length && string.CompareOrdinal(text, start, FUNCTIONS, 0, FUNCTIONS.Length) == 0)
                {
                    awaiting = true;
                    keywordLine = line;
                }
            }
        }

        if (awaiting)
            return (keywordLine, NOT_A_BLOCK, keywordLine);

        return inBlock ? (keywordLine, NEVER_CLOSED, line) : last;
    }

    private static (string? Text, int Line, string? Refusal) Append(string text, string include, string newLine)
    {
        var prefix = text.Length == 0 || text.EndsWith('\n') ? text : text + newLine;
        prefix += newLine;
        var line = prefix.Count(character => character == '\n') + 3;

        return (prefix + FUNCTIONS + newLine + "{" + newLine + include + newLine + "}" + newLine, line, null);
    }

    private static bool IsCommentAt(string text, int index)
    {
        return text[index] == '/' && index + 1 < text.Length && text[index + 1] is '/' or '*';
    }

    /// <summary>The index after <paramref name="end"/> (or at it, when not consumed), counting the lines passed; the text's end when it never comes.</summary>
    private static int SkipTo(string text, int index, string end, ref int line, bool consume)
    {
        var found = text.IndexOf(end, index, StringComparison.Ordinal);
        var stop = found < 0 ? text.Length : consume ? found + end.Length : found;
        line += CountLines(text, index, stop);
        return stop;
    }

    /// <summary>The index after a string's closing quote, escapes honoured, counting the lines passed.</summary>
    private static int SkipString(string text, int index, ref int line)
    {
        var start = index;
        while (index < text.Length && text[index] != '"')
            index += text[index] == '\\' ? 2 : 1;

        var stop = Math.Min(text.Length, index + 1);
        line += CountLines(text, start, stop);
        return stop;
    }

    private static int CountLines(string text, int start, int stop)
    {
        var count = 0;
        for (var index = start; index < stop; index++)
        {
            if (text[index] == '\n')
                count++;
        }

        return count;
    }

    #endregion
}
