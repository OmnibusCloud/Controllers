using System.Globalization;

namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// Makes refusals, and holds what a refusal may show of a text: one token, cut short, on one line.
/// </summary>
internal static class SmtRefusals
{
    #region Constants

    /// <summary>The most characters of a token a refusal shows.</summary>
    public const int TOKEN_CHARACTERS = 64;

    private const string CUT_MARK = "...";
    private const char SPACE = ' ';

    #endregion

    #region Functions

    /// <summary>
    /// Makes a refusal.
    /// </summary>
    /// <param name="code">The code (<see cref="SmtRefusalCodes"/>).</param>
    /// <param name="line">The line it is about.</param>
    /// <param name="command">The name of the command it is about, or empty.</param>
    /// <param name="reason">What is wrong, in words.</param>
    /// <returns>The refusal, with the command's name cut as a token is.</returns>
    public static SmtRefusalData Of(string code, int line, string command, string reason)
    {
        return new SmtRefusalData { Code = code, Line = line, Command = Token(command), Reason = reason };
    }

    /// <summary>
    /// Makes the refusal of a row of a problem set: about the row itself, so without a line of a
    /// text and without a command.
    /// </summary>
    /// <param name="code">The code (<see cref="SmtRefusalCodes"/>).</param>
    /// <param name="reason">What is wrong, in words.</param>
    /// <returns>The refusal; whoever reads the set gives it its row and its id.</returns>
    public static SmtRefusalData OfRow(string code, string reason)
    {
        return new SmtRefusalData { Code = code, Reason = reason };
    }

    /// <summary>
    /// Makes the refusal of a text the reader did not take.
    /// </summary>
    /// <param name="fault">The reader's refusal.</param>
    /// <returns>The refusal, with the reader's line and reason.</returns>
    public static SmtRefusalData Of(SmtSyntaxException fault)
    {
        return new SmtRefusalData { Code = SmtRefusalCodes.Of(fault.Fault), Line = fault.Line, Reason = fault.Reason };
    }

    /// <summary>
    /// What a refusal shows of a token: the token when it is short, its start and a mark when it
    /// is long, on one line that reads as it is written. A blank stands in place of every control
    /// character (a tab, a line break, the C1 controls), every line and paragraph separator, every
    /// format character (the marks and overrides that turn the direction of a text among them, and
    /// the invisible ones), and every half of a surrogate pair that stands alone: a lab's
    /// documents and screens show the refusal as one plain line.
    /// </summary>
    /// <param name="text">A token of the text: a symbol's name, a numeral.</param>
    /// <returns>At most <see cref="TOKEN_CHARACTERS"/> characters.</returns>
    public static string Token(string text)
    {
        var shown = text;
        if (shown.Length > TOKEN_CHARACTERS)
        {
            var kept = TOKEN_CHARACTERS - CUT_MARK.Length;
            if (char.IsHighSurrogate(shown[kept - 1]))
                kept--;

            shown = string.Concat(shown.AsSpan(0, kept), CUT_MARK);
        }

        return NeedsBlanks(shown) ? string.Create(shown.Length, shown, Blank) : shown;
    }

    private static bool NeedsBlanks(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (IsBlanked(text, index))
                return true;
        }

        return false;
    }

    private static void Blank(Span<char> target, string source)
    {
        for (var index = 0; index < source.Length; index++)
            target[index] = IsBlanked(source, index) ? SPACE : source[index];
    }

    /// <summary>
    /// Whether a character of a text is shown as a blank. Each half of a whole surrogate pair is
    /// judged by the character the pair makes, so that both halves go or both stay.
    /// </summary>
    private static bool IsBlanked(string text, int index)
    {
        var character = text[index];
        if (char.IsHighSurrogate(character))
            return index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]) || IsHidden(CharUnicodeInfo.GetUnicodeCategory(text, index));

        if (char.IsLowSurrogate(character))
            return index == 0 || !char.IsHighSurrogate(text[index - 1]) || IsHidden(CharUnicodeInfo.GetUnicodeCategory(text, index - 1));

        return IsHidden(CharUnicodeInfo.GetUnicodeCategory(character));
    }

    private static bool IsHidden(UnicodeCategory category)
    {
        return category is UnicodeCategory.Control or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.Format;
    }

    #endregion
}
