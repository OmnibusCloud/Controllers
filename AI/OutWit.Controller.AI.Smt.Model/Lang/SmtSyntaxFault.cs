namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// Why a text was not read as SMT-LIB: what is wrong with it, or which limit of the reader it is past.
/// </summary>
public enum SmtSyntaxFault
{
    /// <summary>A character the lexicon has no place for where it stands: a control character, a character outside ASCII in a bare token, a backslash in a quoted symbol, half of a surrogate pair.</summary>
    BadCharacter = 0,

    /// <summary>A closing parenthesis with no list open, or a list still open at the end of the text.</summary>
    UnbalancedParenthesis = 1,

    /// <summary>A string literal or a quoted symbol still open at the end of the text.</summary>
    UnclosedLiteral = 2,

    /// <summary>A token that is none of the lexicon's: digits followed by letters, a numeral with a leading zero, <c>#x</c> without digits, a lone colon.</summary>
    MalformedToken = 3,

    /// <summary>The text is longer than the reader was told to take.</summary>
    TextTooLong = 4,

    /// <summary>More tokens than the limit.</summary>
    TooManyTokens = 5,

    /// <summary>Lists nested deeper than the limit.</summary>
    NestingTooDeep = 6,

    /// <summary>A numeral or a decimal of more digits than the limit.</summary>
    NumeralTooLong = 7,

    /// <summary>A hexadecimal or binary literal of more bits than the limit.</summary>
    LiteralTooWide = 8,

    /// <summary>A string literal longer than the limit.</summary>
    StringTooLong = 9
}
