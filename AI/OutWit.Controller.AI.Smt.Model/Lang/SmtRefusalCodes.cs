namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// The codes of a refusal (<see cref="SmtRefusalData.Code"/>): fixed strings, a public contract.
/// A code names the rule a problem or a term broke; the refusal's reason says it in words.
/// </summary>
public static class SmtRefusalCodes
{
    #region Constants

    /// <summary>The text is not SMT-LIB: a character the lexicon has no place for, an unbalanced parenthesis, a literal left open, a token that is none of the lexicon's.</summary>
    public const string SYNTAX_ERROR = "syntax_error";

    /// <summary>The text has more characters than a problem, or a term, may have.</summary>
    public const string TEXT_TOO_LONG = "text_too_long";

    /// <summary>The text has more tokens than a problem may have.</summary>
    public const string TOO_MANY_TOKENS = "too_many_tokens";

    /// <summary>Lists are nested deeper than a problem may nest them.</summary>
    public const string NESTING_TOO_DEEP = "nesting_too_deep";

    /// <summary>A numeral or a decimal has more digits than allowed.</summary>
    public const string NUMERAL_TOO_LONG = "numeral_too_long";

    /// <summary>A hexadecimal or binary literal is wider than the widest bit-vector.</summary>
    public const string LITERAL_TOO_WIDE = "literal_too_wide";

    /// <summary>A string literal is longer than allowed.</summary>
    public const string STRING_TOO_LONG = "string_too_long";

    /// <summary>A top-level form is no command: an atom, an empty list, or a list that does not start with a symbol.</summary>
    public const string NOT_A_COMMAND = "not_a_command";

    /// <summary>A command a problem may not hold: anything but a declaration, a definition, an assertion or what is stripped; a command name between bars.</summary>
    public const string COMMAND_NOT_ALLOWED = "command_not_allowed";

    /// <summary>A command that states the problem after its end: after <c>check-sat</c> the script would be an incremental one, after <c>exit</c> a solver runs nothing.</summary>
    public const string COMMAND_AFTER_END = "command_after_end";

    /// <summary>A command a problem may hold, written in another shape than the standard gives it.</summary>
    public const string COMMAND_MALFORMED = "command_malformed";

    /// <summary>A general reserved word between bars: an ordinary symbol by the standard, the reserved word to the solver.</summary>
    public const string RESERVED_SYMBOL = "reserved_symbol";

    /// <summary>A symbol that starts with a minus sign and a digit: a symbol by the standard, a negative number to the solver.</summary>
    public const string AMBIGUOUS_TOKEN = "ambiguous_token";

    /// <summary>A string literal with a character outside printable ASCII, the tab and the line breaks.</summary>
    public const string NON_ASCII_STRING = "non_ascii_string";

    /// <summary>An index of an indexed identifier above the widest bit-vector.</summary>
    public const string INDEX_TOO_LARGE = "index_too_large";

    /// <summary>More declared names than a problem may have.</summary>
    public const string TOO_MANY_SYMBOLS = "too_many_symbols";

    /// <summary>A declared name that starts with the prefix kept for the controller's own names.</summary>
    public const string RESERVED_PREFIX = "reserved_prefix";

    /// <summary>A text given as one term is not one s-expression: it holds none, or more than one.</summary>
    public const string NOT_A_TERM = "not_a_term";

    private static readonly HashSet<string> CODES = new(StringComparer.Ordinal)
    {
        SYNTAX_ERROR, TEXT_TOO_LONG, TOO_MANY_TOKENS, NESTING_TOO_DEEP, NUMERAL_TOO_LONG, LITERAL_TOO_WIDE, STRING_TOO_LONG,
        NOT_A_COMMAND, COMMAND_NOT_ALLOWED, COMMAND_AFTER_END, COMMAND_MALFORMED, RESERVED_SYMBOL, AMBIGUOUS_TOKEN, NON_ASCII_STRING,
        INDEX_TOO_LARGE, TOO_MANY_SYMBOLS, RESERVED_PREFIX, NOT_A_TERM
    };

    #endregion

    #region Functions

    /// <summary>
    /// The code of a fault of the reader.
    /// </summary>
    /// <param name="fault">What the reader refused a text for.</param>
    /// <returns>The limit's own code for a limit, <see cref="SYNTAX_ERROR"/> for anything else.</returns>
    public static string Of(SmtSyntaxFault fault)
    {
        return fault switch
        {
            SmtSyntaxFault.TextTooLong => TEXT_TOO_LONG,
            SmtSyntaxFault.TooManyTokens => TOO_MANY_TOKENS,
            SmtSyntaxFault.NestingTooDeep => NESTING_TOO_DEEP,
            SmtSyntaxFault.NumeralTooLong => NUMERAL_TOO_LONG,
            SmtSyntaxFault.LiteralTooWide => LITERAL_TOO_WIDE,
            SmtSyntaxFault.StringTooLong => STRING_TOO_LONG,
            _ => SYNTAX_ERROR
        };
    }

    #endregion

    #region Properties

    /// <summary>Every code.</summary>
    public static IReadOnlyCollection<string> All => CODES;

    #endregion
}
