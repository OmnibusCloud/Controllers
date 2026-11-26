namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// What a bare token is: a run of printable ASCII between two delimiters, read as a simple
/// symbol, a keyword, a numeral, a decimal, or a hexadecimal or binary literal in full. A token
/// that is none of them in full is refused, never read as the nearest one: <c>12abc</c> is no
/// numeral followed by a symbol, <c>007</c> is no seven.
/// </summary>
internal static class SmtBareToken
{
    #region Constants

    private const char COLON = ':';
    private const char HASH = '#';
    private const char DOT = '.';
    private const char ZERO = '0';
    private const char HEXADECIMAL_MARK = 'x';
    private const char BINARY_MARK = 'b';
    private const int BITS_PER_HEXADECIMAL_DIGIT = 4;
    private const int LITERAL_PREFIX_LENGTH = 2;

    #endregion

    #region Functions

    /// <summary>
    /// Makes the atom of a bare token.
    /// </summary>
    /// <param name="token">The token: not empty, of printable ASCII, without a delimiter.</param>
    /// <param name="limits">The reader's limits.</param>
    /// <param name="line">The line the token starts on.</param>
    /// <param name="column">The column the token starts at.</param>
    /// <returns>The atom.</returns>
    /// <exception cref="SmtSyntaxException">The token is none of the lexicon's, or it is past a limit.</exception>
    public static SmtExpression Classify(ReadOnlySpan<char> token, SmtReaderLimits limits, int line, int column)
    {
        var first = token[0];

        if (first == COLON)
            return Keyword(token, line, column);

        if (first == HASH)
            return Literal(token, limits, line, column);

        if (char.IsAsciiDigit(first))
            return Number(token, limits, line, column);

        if (!SmtLexicon.IsSimpleSymbol(token))
        {
            var bad = token[FirstOutsideSimpleSymbol(token)];
            throw new SmtSyntaxException(SmtSyntaxFault.BadCharacter, line, column, $"{SmtLexicon.Describe(bad)} cannot stand in a simple symbol");
        }

        return SmtExpression.Symbol(token.ToString(), line);
    }

    private static SmtExpression Keyword(ReadOnlySpan<char> token, int line, int column)
    {
        if (!SmtLexicon.IsSimpleSymbol(token[1..]))
            throw new SmtSyntaxException(SmtSyntaxFault.MalformedToken, line, column, "a keyword is a colon and a simple symbol");

        return SmtExpression.Token(SmtExpressionKind.Keyword, token.ToString(), line);
    }

    private static SmtExpression Literal(ReadOnlySpan<char> token, SmtReaderLimits limits, int line, int column)
    {
        var mark = token.Length > 1 ? token[1] : default;
        var digits = token.Length > LITERAL_PREFIX_LENGTH ? token[LITERAL_PREFIX_LENGTH..] : default;

        if (mark == HEXADECIMAL_MARK && !digits.IsEmpty && All(digits, char.IsAsciiHexDigit))
        {
            if ((long)digits.Length * BITS_PER_HEXADECIMAL_DIGIT > limits.MaxLiteralBits)
                throw TooWide(limits, line, column);

            return SmtExpression.Token(SmtExpressionKind.Hexadecimal, token.ToString(), line);
        }

        if (mark == BINARY_MARK && !digits.IsEmpty && All(digits, SmtLexicon.IsBinaryDigit))
        {
            if (digits.Length > limits.MaxLiteralBits)
                throw TooWide(limits, line, column);

            return SmtExpression.Token(SmtExpressionKind.Binary, token.ToString(), line);
        }

        throw new SmtSyntaxException(SmtSyntaxFault.MalformedToken, line, column, "a literal that starts with # is #x and hexadecimal digits, or #b and binary digits");
    }

    private static SmtExpression Number(ReadOnlySpan<char> token, SmtReaderLimits limits, int line, int column)
    {
        var dot = token.IndexOf(DOT);
        var whole = dot < 0 ? token : token[..dot];
        var fraction = dot < 0 ? default : token[(dot + 1)..];

        if (!All(whole, char.IsAsciiDigit) || (dot >= 0 && (fraction.IsEmpty || !All(fraction, char.IsAsciiDigit))))
            throw new SmtSyntaxException(SmtSyntaxFault.MalformedToken, line, column, "a token that starts with a digit is a numeral, or a decimal with digits on both sides of one dot");

        if (whole.Length > 1 && whole[0] == ZERO)
            throw new SmtSyntaxException(SmtSyntaxFault.MalformedToken, line, column, "a numeral with a leading zero");

        if (whole.Length + fraction.Length > limits.MaxNumeralDigits)
            throw new SmtSyntaxException(SmtSyntaxFault.NumeralTooLong, line, column, $"a number has more than the {limits.MaxNumeralDigits} digits allowed");

        return SmtExpression.Token(dot < 0 ? SmtExpressionKind.Numeral : SmtExpressionKind.Decimal, token.ToString(), line);
    }

    private static SmtSyntaxException TooWide(SmtReaderLimits limits, int line, int column)
    {
        return new SmtSyntaxException(SmtSyntaxFault.LiteralTooWide, line, column, $"a literal is wider than the {limits.MaxLiteralBits} bits allowed");
    }

    private static bool All(ReadOnlySpan<char> text, Func<char, bool> test)
    {
        foreach (var character in text)
        {
            if (!test(character))
                return false;
        }

        return true;
    }

    private static int FirstOutsideSimpleSymbol(ReadOnlySpan<char> token)
    {
        for (var index = 0; index < token.Length; index++)
        {
            if (!SmtLexicon.IsSimpleSymbolCharacter(token[index]))
                return index;
        }

        return 0;
    }

    #endregion
}
