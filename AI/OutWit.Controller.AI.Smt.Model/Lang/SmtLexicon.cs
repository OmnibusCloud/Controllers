namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// The character classes of the SMT-LIB 2.6 lexicon, as the reader and the writer of
/// s-expressions share them.
/// </summary>
public static class SmtLexicon
{
    #region Constants

    private const string SIMPLE_SYMBOL_MARKS = "~!@$%^&*_-+=<>.?/";
    private const char SPACE = ' ';
    private const char DELETE = (char)127;

    #endregion

    #region Functions

    /// <summary>
    /// Whether a character separates tokens and is nothing else: the space, the tab, the line
    /// feed or the carriage return. No other character is a blank, whatever Unicode calls it.
    /// </summary>
    /// <param name="character">The character.</param>
    /// <returns>True for a blank.</returns>
    public static bool IsBlank(char character)
    {
        return character is ' ' or '\t' or '\n' or '\r';
    }

    /// <summary>
    /// Whether a character ends a bare token: a blank, a parenthesis, the quote of a string, the
    /// bar of a quoted symbol, or the semicolon of a comment.
    /// </summary>
    /// <param name="character">The character.</param>
    /// <returns>True for a character no bare token holds.</returns>
    public static bool IsDelimiter(char character)
    {
        return IsBlank(character) || character is '(' or ')' or '"' or ';' or '|';
    }

    /// <summary>
    /// Whether a character may stand in a simple symbol: an ASCII letter or digit, or one of
    /// <c>~ ! @ $ % ^ &amp; * _ - + = &lt; &gt; . ? /</c>.
    /// </summary>
    /// <param name="character">The character.</param>
    /// <returns>True for a character of a simple symbol.</returns>
    public static bool IsSimpleSymbolCharacter(char character)
    {
        return char.IsAsciiLetterOrDigit(character) || SIMPLE_SYMBOL_MARKS.Contains(character);
    }

    /// <summary>
    /// Whether a text has the shape of a simple symbol: not empty, of the characters of one, and
    /// not starting with a digit. A reserved word has that shape too.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>True when the text can be written without bars.</returns>
    public static bool IsSimpleSymbol(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty || char.IsAsciiDigit(text[0]))
            return false;

        foreach (var character in text)
        {
            if (!IsSimpleSymbolCharacter(character))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether a character is a digit of a binary literal.
    /// </summary>
    /// <param name="character">The character.</param>
    /// <returns>True for <c>0</c> and <c>1</c>.</returns>
    public static bool IsBinaryDigit(char character)
    {
        return character is '0' or '1';
    }

    /// <summary>
    /// Names a character for a refusal: by its code point when it is a control character or
    /// outside ASCII, by itself when it is printable ASCII.
    /// </summary>
    /// <param name="character">The character.</param>
    /// <returns>Words such as <c>the control character U+000C</c> or <c>the character '''</c>.</returns>
    internal static string Describe(char character)
    {
        if (character < SPACE || character == DELETE)
            return $"the control character U+{(int)character:X4}";

        return character > DELETE ? $"the character U+{(int)character:X4}" : $"the character '{character}'";
    }

    #endregion
}
