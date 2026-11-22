namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// What an SMT-LIB 2.6 s-expression is: a list, or one of the lexicon's atoms.
/// </summary>
public enum SmtExpressionKind
{
    /// <summary>A parenthesised sequence of s-expressions.</summary>
    List = 0,

    /// <summary>A symbol, simple or written between bars; a reserved word is a symbol too.</summary>
    Symbol = 1,

    /// <summary>A keyword: a colon and a simple symbol (<c>:status</c>).</summary>
    Keyword = 2,

    /// <summary>A numeral: <c>0</c>, or digits that do not start with <c>0</c>.</summary>
    Numeral = 3,

    /// <summary>A decimal: a numeral, a dot and one digit or more.</summary>
    Decimal = 4,

    /// <summary>A hexadecimal literal: <c>#x</c> and one hexadecimal digit or more.</summary>
    Hexadecimal = 5,

    /// <summary>A binary literal: <c>#b</c> and one binary digit or more.</summary>
    Binary = 6,

    /// <summary>A string literal.</summary>
    String = 7
}
