using System.Globalization;
using System.Numerics;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Profiles;

namespace OutWit.Controller.AI.Smt.Solver;

/// <summary>
/// Reads what the solver answers to one command. An answer is the text the solver wrote between
/// two of its reads, so nothing has to be searched for in it: it is the answer of the command
/// sent last, whole. Nothing here throws for what an answer holds: an answer that is not what
/// its command is answered with is said to be unreadable, and the session ends on it.
/// <para>
/// The answers that are read as s-expressions (the statistics, the reason of an unknown, the
/// values of terms) are read by the reader of the model package under limits of the session's
/// own. An error is read as text: the solver writes a quote inside its message with a
/// backslash before it, which is not how SMT-LIB writes one.
/// </para>
/// </summary>
internal static class SmtAnswers
{
    #region Constants

    /// <summary>The most characters kept of a message of the solver's.</summary>
    public const int MESSAGE_CHARACTERS = 512;

    private const string SAT = "sat";
    private const string UNSAT = "unsat";
    private const string UNKNOWN = "unknown";

    private const string ERROR_HEAD = "(error";
    private const string ERROR_OPEN = "(error \"";
    private const string ERROR_CLOSE = "\")";

    private const string REASON_KEYWORD = ":reason-unknown";
    private const string REASON_OPEN = "(:reason-unknown \"";
    private const string RESOURCE_COUNT_KEYWORD = ":rlimit-count";

    private const char LINE_FEED = '\n';
    private const char DECIMAL_POINT = '.';

    /// <summary>
    /// How much of an answer the reader takes. Its characters are bounded by the session's output
    /// cap already; the tokens and the nesting are those of a submitted problem, since a value
    /// read here goes back to the solver inside an assertion.
    /// </summary>
    private static readonly SmtReaderLimits LIMITS = new()
    {
        MaxCharacters = SmtProfile.OUTPUT_LIMIT_BYTES,
        MaxTokens = SmtReaderLimits.PROBLEM_TOKENS,
        MaxDepth = SmtReaderLimits.PROBLEM_DEPTH
    };

    #endregion

    #region Functions

    /// <summary>
    /// Whether the solver said nothing: the answer of a command that only states something.
    /// </summary>
    /// <param name="answer">The answer.</param>
    /// <returns>True for an answer of blanks.</returns>
    public static bool IsBlank(string answer)
    {
        return string.IsNullOrWhiteSpace(answer);
    }

    /// <summary>
    /// Whether the answer is an error of the solver's: it starts with <c>(error</c>.
    /// </summary>
    /// <param name="answer">The answer.</param>
    /// <returns>True for an error.</returns>
    public static bool IsError(string answer)
    {
        return answer.AsSpan().TrimStart().StartsWith(ERROR_HEAD, StringComparison.Ordinal);
    }

    /// <summary>
    /// The message of an answer that should not have been given: the text of the solver's first
    /// error without what is written around it, or the answer itself when it is no error, cut to
    /// <see cref="MESSAGE_CHARACTERS"/> characters.
    /// </summary>
    /// <param name="answer">The answer.</param>
    /// <returns>The message.</returns>
    public static string Message(string answer)
    {
        var text = answer.Trim();
        if (text.StartsWith(ERROR_OPEN, StringComparison.Ordinal))
        {
            // Up to the first quote that closes a line: a message can hold a quote of its own.
            var close = ErrorEnd(text);
            text = close < 0 ? text[ERROR_OPEN.Length..] : text[ERROR_OPEN.Length..close];
        }

        return Cut(text.Trim(), MESSAGE_CHARACTERS);
    }

    /// <summary>
    /// Reads the answer of <c>(check-sat)</c>.
    /// </summary>
    /// <param name="answer">The answer.</param>
    /// <returns>The verdict; <see cref="SmtVerdict.None"/> when the answer is none of the three words.</returns>
    public static SmtVerdict Verdict(string answer)
    {
        return answer.AsSpan().Trim() switch
        {
            SAT => SmtVerdict.Sat,
            UNSAT => SmtVerdict.Unsat,
            UNKNOWN => SmtVerdict.Unknown,
            _ => SmtVerdict.None
        };
    }

    /// <summary>
    /// Reads the answer of <c>(get-info :reason-unknown)</c>: the solver's own word for why a
    /// check has no verdict.
    /// </summary>
    /// <param name="answer">The answer.</param>
    /// <returns>The word, cut to <see cref="MESSAGE_CHARACTERS"/> characters; the answer itself, cut, when it is not in the shape of that answer.</returns>
    public static string Reason(string answer)
    {
        var text = answer.Trim();
        var read = Read(text);
        if (read is [{ IsList: true, Items: [{ Kind: SmtExpressionKind.Keyword, Text: REASON_KEYWORD }, { IsList: false } word] }])
            return Cut(word.Text, MESSAGE_CHARACTERS);

        if (read is [{ IsList: true, Items: [{ Kind: SmtExpressionKind.Keyword, Text: REASON_KEYWORD }, { IsList: true } list] }])
            return Cut(list.ToString(), MESSAGE_CHARACTERS);

        if (text.StartsWith(REASON_OPEN, StringComparison.Ordinal) && text.EndsWith(ERROR_CLOSE, StringComparison.Ordinal) && text.Length >= REASON_OPEN.Length + ERROR_CLOSE.Length)
            text = text[REASON_OPEN.Length..^ERROR_CLOSE.Length];

        return Cut(text, MESSAGE_CHARACTERS);
    }

    /// <summary>
    /// Reads the solver's resource counter out of the answer of <c>(get-info :all-statistics)</c>:
    /// a list of keywords, each followed by its value. The solver leaves a counter that is zero
    /// out, and writes one above 4,294,967,295 as a decimal.
    /// </summary>
    /// <param name="answer">The answer.</param>
    /// <returns>The counter, 0 when the statistics do not hold it; null when the answer is not statistics.</returns>
    public static long? ResourceCount(string answer)
    {
        if (Read(answer) is not [{ IsList: true } statistics] || statistics.Items.Count % 2 != 0)
            return null;

        long count = 0;
        for (var index = 0; index < statistics.Items.Count; index += 2)
        {
            var key = statistics.Items[index];
            var value = statistics.Items[index + 1];
            if (key.Kind != SmtExpressionKind.Keyword || value.IsList)
                return null;

            if (!string.Equals(key.Text, RESOURCE_COUNT_KEYWORD, StringComparison.Ordinal))
                continue;

            if (Whole(value) is not { } number)
                return null;

            count = number;
        }

        return count;
    }

    /// <summary>
    /// Reads the answer of <c>(get-value (t1 ... tn))</c>: a list of pairs, each a term as it was
    /// asked and its value.
    /// </summary>
    /// <param name="answer">The answer.</param>
    /// <param name="terms">The terms that were asked, in order.</param>
    /// <returns>The value of each term, in the same order; null when the answer is not the values of exactly these terms.</returns>
    public static IReadOnlyList<SmtExpression>? Values(string answer, IReadOnlyList<SmtExpression> terms)
    {
        if (Read(answer) is not [{ IsList: true } pairs] || pairs.Items.Count != terms.Count)
            return null;

        var values = new SmtExpression[terms.Count];
        for (var index = 0; index < values.Length; index++)
        {
            if (pairs.Items[index] is not { IsList: true, Items: [var term, var value] } || !term.Is(terms[index]))
                return null;

            values[index] = value;
        }

        return values;
    }

    #endregion

    #region Tools

    /// <summary>The s-expressions of an answer, or null when it is not SMT-LIB or past a limit.</summary>
    private static IReadOnlyList<SmtExpression>? Read(string answer)
    {
        try
        {
            return SmtReader.Read(answer, LIMITS);
        }
        catch (SmtSyntaxException)
        {
            return null;
        }
    }

    /// <summary>A counter's value: a numeral, or a decimal whose whole part is taken.</summary>
    private static long? Whole(SmtExpression value)
    {
        var digits = value.Kind switch
        {
            SmtExpressionKind.Numeral => value.Text,
            SmtExpressionKind.Decimal => value.Text[..value.Text.IndexOf(DECIMAL_POINT)],
            _ => null
        };

        if (digits == null || !BigInteger.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number > long.MaxValue)
            return null;

        return (long)number;
    }

    /// <summary>Where the first error of a text ends: at the first quote and parenthesis that close a line.</summary>
    private static int ErrorEnd(string text)
    {
        var from = ERROR_OPEN.Length;
        while (from <= text.Length)
        {
            var close = text.IndexOf(ERROR_CLOSE, from, StringComparison.Ordinal);
            if (close < 0)
                return -1;

            var after = close + ERROR_CLOSE.Length;
            if (after == text.Length || text[after] == LINE_FEED || text[after] == '\r')
                return close;

            from = close + 1;
        }

        return -1;
    }

    /// <summary>A text cut to a number of characters, never between the two halves of a surrogate pair.</summary>
    private static string Cut(string text, int characters)
    {
        if (text.Length <= characters)
            return text;

        var length = char.IsHighSurrogate(text[characters - 1]) ? characters - 1 : characters;
        return text[..length];
    }

    #endregion
}
