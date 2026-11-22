namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The characters a string literal of the theory of strings stands for. The standard has two
/// escapes: a backslash, <c>u</c> and four hexadecimal digits; and a backslash, <c>u</c> and one
/// to five hexadecimal digits between braces, up to <c>2FFFF</c>. Every other character stands
/// for itself, a backslash included.
/// <para>
/// Readers differ exactly where a literal leaves that: a backslash and <c>u</c> that start no
/// escape of the standard, and a character outside printable ASCII (a solver that reads bytes
/// takes one character of the text for several). Such a literal is not decoded: what it means
/// depends on who reads it.
/// </para>
/// </summary>
internal static class SmtStringLiteral
{
    #region Constants

    private const char BACKSLASH = '\\';
    private const char ESCAPE_MARK = 'u';
    private const char OPEN = '{';
    private const char CLOSE = '}';
    private const char FIRST_PRINTABLE = ' ';
    private const char LAST_PRINTABLE = '~';
    private const int PLAIN_ESCAPE_DIGITS = 4;
    private const int MAX_BRACED_DIGITS = 5;
    private const int HEXADECIMAL_BASE = 16;
    private const int DECIMAL_DIGITS = 10;

    #endregion

    #region Functions

    /// <summary>
    /// Decodes the content of a string literal.
    /// </summary>
    /// <param name="content">The literal's content, a doubled quote already one quote.</param>
    /// <param name="codePoints">The characters, as code points.</param>
    /// <returns>True when every reader reads the literal the same way.</returns>
    public static bool TryDecode(string content, out int[] codePoints)
    {
        codePoints = [];
        var decoded = new List<int>(content.Length);
        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (character is < FIRST_PRINTABLE or > LAST_PRINTABLE)
                return false;

            if (character != BACKSLASH || index + 1 >= content.Length || content[index + 1] != ESCAPE_MARK)
            {
                decoded.Add(character);
                continue;
            }

            if (!TryEscape(content, index + 2, out var codePoint, out var length))
                return false;

            // The backslash is passed by the loop, the u and the escape's own characters here.
            decoded.Add(codePoint);
            index += 1 + length;
        }

        codePoints = decoded.ToArray();
        return true;
    }

    /// <summary>
    /// Reads what follows a backslash and <c>u</c> as an escape of the standard.
    /// </summary>
    /// <param name="content">The literal's content.</param>
    /// <param name="start">The position after the <c>u</c>.</param>
    /// <param name="codePoint">The character the escape stands for.</param>
    /// <param name="length">How many characters after the <c>u</c> belong to the escape.</param>
    private static bool TryEscape(string content, int start, out int codePoint, out int length)
    {
        codePoint = 0;
        length = 0;
        if (start < content.Length && content[start] == OPEN)
        {
            var digits = 0;
            while (digits <= MAX_BRACED_DIGITS && start + 1 + digits < content.Length && char.IsAsciiHexDigit(content[start + 1 + digits]))
            {
                codePoint = codePoint * HEXADECIMAL_BASE + DigitValue(content[start + 1 + digits]);
                digits++;
            }

            var close = start + 1 + digits;
            if (digits is < 1 or > MAX_BRACED_DIGITS || close >= content.Length || content[close] != CLOSE || codePoint > SmtStrings.MAX_CODE_POINT)
                return false;

            length = digits + 2;
            return true;
        }

        if (start + PLAIN_ESCAPE_DIGITS > content.Length)
            return false;

        for (var index = start; index < start + PLAIN_ESCAPE_DIGITS; index++)
        {
            if (!char.IsAsciiHexDigit(content[index]))
                return false;

            codePoint = codePoint * HEXADECIMAL_BASE + DigitValue(content[index]);
        }

        length = PLAIN_ESCAPE_DIGITS;
        return true;
    }

    private static int DigitValue(char digit)
    {
        return char.IsAsciiDigit(digit) ? digit - '0' : char.ToLowerInvariant(digit) - 'a' + DECIMAL_DIGITS;
    }

    #endregion
}
