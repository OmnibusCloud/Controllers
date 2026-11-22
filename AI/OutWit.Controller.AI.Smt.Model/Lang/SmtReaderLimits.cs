namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// How much the reader takes before it refuses a text. The limits are counted while reading, so
/// a text past one of them costs no more than the part read up to it.
/// </summary>
public sealed class SmtReaderLimits
{
    #region Constants

    /// <summary>The most characters of one problem: four megabytes of ASCII.</summary>
    public const int PROBLEM_CHARACTERS = 4 * 1024 * 1024;

    /// <summary>The most tokens of one problem, parentheses included.</summary>
    public const int PROBLEM_TOKENS = 2_000_000;

    /// <summary>
    /// The deepest nesting of lists in one problem. A term that is used many times is written as
    /// a chain of bindings, one inside the other: real problems nest thousands of levels deep.
    /// </summary>
    public const int PROBLEM_DEPTH = 100_000;

    /// <summary>The most digits of one numeral or decimal of a problem.</summary>
    public const int PROBLEM_NUMERAL_DIGITS = 4_096;

    /// <summary>The widest hexadecimal or binary literal of a problem, in bits: the widest bit-vector.</summary>
    public const int PROBLEM_LITERAL_BITS = 65_536;

    /// <summary>The longest string literal of a problem, in characters.</summary>
    public const int PROBLEM_STRING_CHARACTERS = 65_536;

    #endregion

    #region Properties

    /// <summary>The limits a problem somebody submitted is read under.</summary>
    public static SmtReaderLimits Problem { get; } = new()
    {
        MaxCharacters = PROBLEM_CHARACTERS,
        MaxTokens = PROBLEM_TOKENS,
        MaxDepth = PROBLEM_DEPTH,
        MaxNumeralDigits = PROBLEM_NUMERAL_DIGITS,
        MaxLiteralBits = PROBLEM_LITERAL_BITS,
        MaxStringCharacters = PROBLEM_STRING_CHARACTERS
    };

    /// <summary>
    /// No limit of the reader's own: for a text whose size something else has already bounded.
    /// </summary>
    public static SmtReaderLimits Unbounded { get; } = new();

    /// <summary>The most characters of the whole text.</summary>
    public int MaxCharacters { get; init; } = int.MaxValue;

    /// <summary>The most tokens: every atom and every parenthesis is one.</summary>
    public int MaxTokens { get; init; } = int.MaxValue;

    /// <summary>The deepest nesting of lists: a top-level list is at depth 1.</summary>
    public int MaxDepth { get; init; } = int.MaxValue;

    /// <summary>The most digits of a numeral, or of a decimal (both sides of its dot together).</summary>
    public int MaxNumeralDigits { get; init; } = int.MaxValue;

    /// <summary>The widest hexadecimal or binary literal, in bits (four a hexadecimal digit).</summary>
    public int MaxLiteralBits { get; init; } = int.MaxValue;

    /// <summary>The longest string literal, in characters of its content.</summary>
    public int MaxStringCharacters { get; init; } = int.MaxValue;

    #endregion
}
