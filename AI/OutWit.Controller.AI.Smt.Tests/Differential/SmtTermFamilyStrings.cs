using System.Text;

namespace OutWit.Controller.AI.Smt.Tests.Differential;

/// <summary>
/// Strings: the operators the evaluator computes, at their edges: the empty string, positions
/// before and past the end, negative lengths, a code past the last character, digits with a
/// leading zero. Literals hold the escapes of the standard and backslashes that start none.
/// Now and then an operator the evaluator does not compute, so that its reason is counted.
/// </summary>
internal sealed class SmtTermFamilyStrings : SmtTermFamily
{
    #region Constants

    private static readonly string[] PIECES =
    [
        "a", "b", "c", "ab", "A", "0", "1", "7", "9", " ", "\"\"", "\\", "\\n", "\\t", "\\x41", "\\\\",
        "\\u{0}", "\\u{7f}", "\\u{e9}", "\\u{41}", "\\u0042", "\\u{2FFFF}", "\\u{d800}", "\\u{1F600}", "~", "{", "}",
        "\\u{5c}", "\\u{5c}u{41}", "\\u{5C}u0042", "u{42}"
    ];

    private static readonly string[] ODD_PIECES = ["\\u{30000}", "\\u{}", "\\u12", "\\u{123456}", "\\uZZZZ"];

    private static readonly string[] NUMBERS = ["\"007\"", "\"\"", "\"12a\"", "\"42\"", "\"0\"", "\"-5\"", "\" 3\"", "\"18446744073709551617\""];

    private static readonly int[] POSITIONS = [-1, 0, 1, 2, 3, 5, 100];

    private static readonly int[] CODES = [-1, 0, 48, 57, 65, 92, 127, 233, 55296, 196607, 196608, 1114111];

    private static readonly string[] PREDICATES = ["str.prefixof", "str.suffixof", "str.contains", "str.<", "str.<="];

    /// <summary>The name a string is bound to when positions are taken from its length.</summary>
    private const string BOUND = "s1";

    private static readonly string[] EDGES = ["(str.len s1)", "(+ (str.len s1) 1)", "(- (str.len s1) 1)", "(div (str.len s1) 2)"];

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the family.
    /// </summary>
    /// <param name="generator">The generator the subterms come from.</param>
    public SmtTermFamilyStrings(SmtTermGenerator generator)
        : base(generator)
    {
    }

    #endregion

    #region Functions

    /// <inheritdoc />
    public override string? Produce(SmtGenSort sort, int depth)
    {
        return sort.Kind switch
        {
            SmtGenSortKind.String => Text(depth - 1),
            SmtGenSortKind.Int => Number(depth - 1),
            SmtGenSortKind.Bool => Predicate(depth - 1),
            _ => null
        };
    }

    /// <inheritdoc />
    public override string? Literal(SmtGenSort sort)
    {
        if (sort.Kind != SmtGenSortKind.String)
            return null;

        var generator = Generator;
        var builder = new StringBuilder("\"");
        var pieces = generator.Next(5) == 0 ? 0 : generator.Between(1, 4);
        for (var index = 0; index < pieces; index++)
            builder.Append(generator.Chance(3) ? generator.Pick(ODD_PIECES) : generator.Pick(PIECES));

        return builder.Append('"').ToString();
    }

    private string Of(int depth)
    {
        return Generator.Term(SmtGenSort.STRING, depth);
    }

    /// <summary>
    /// A position: one of a few numbers, any integer term, or, for a string bound to
    /// <see cref="BOUND"/>, its length, one past it, one before it, its half.
    /// </summary>
    private string Position(int depth, bool bound = false)
    {
        var generator = Generator;
        if (bound && generator.Chance(60))
            return generator.Pick(EDGES);

        return generator.Chance(60) ? SmtTermGenerator.Integer(generator.Pick(POSITIONS)) : generator.Term(SmtGenSort.INT, depth);
    }

    /// <summary>
    /// An operator applied to a string and to positions in it: half the time the string is
    /// bound first, so that a position can be its length or next to it.
    /// </summary>
    private string AtPositions(string name, int depth, Func<bool, string> rest)
    {
        var text = Of(depth);
        return Generator.Chance(50) ? $"({name} {text} {rest(false)})" : $"(let (({BOUND} {text})) ({name} {BOUND} {rest(true)}))";
    }

    /// <summary>A part to look for: the empty string often, since it is found everywhere.</summary>
    private string Part(int depth)
    {
        return Generator.Chance(30) ? "\"\"" : Of(depth);
    }

    private string Text(int depth)
    {
        var generator = Generator;
        switch (generator.Next(9))
        {
            case 0:
            case 1:
                return $"(str.++ {generator.Terms(SmtGenSort.STRING, depth, 2, 3)})";
            case 2:
                return AtPositions("str.at", depth, bound => Position(depth, bound));
            case 3:
                return AtPositions("str.substr", depth, bound => $"{Position(depth, bound)} {Position(depth, bound)}");
            case 4:
                return $"(str.replace {Of(depth)} {Part(depth)} {Of(depth)})";
            case 5:
                return $"(str.from_int {(generator.Chance(50) ? SmtTermGenerator.Integer(generator.IntegerValue()) : generator.Term(SmtGenSort.INT, depth))})";
            case 6:
                return $"(str.from_code {(generator.Chance(60) ? SmtTermGenerator.Integer(generator.Pick(CODES)) : generator.Term(SmtGenSort.INT, depth))})";
            case 7 when generator.Chance(25):
                return $"(str.replace_all {Of(depth)} {Of(depth)} {Of(depth)})";
            default:
                return $"(str.++ {Of(depth)} {Of(depth)})";
        }
    }

    private string Number(int depth)
    {
        var generator = Generator;
        switch (generator.Next(5))
        {
            case 0:
            case 1:
                return $"(str.len {Of(depth)})";
            case 2:
                return AtPositions("str.indexof", depth, bound => $"{Part(depth)} {Position(depth, bound)}");
            case 3:
                return $"(str.to_int {(generator.Chance(50) ? generator.Pick(NUMBERS) : Of(depth))})";
            default:
                return $"(str.to_code {Of(depth)})";
        }
    }

    private string Predicate(int depth)
    {
        var generator = Generator;
        switch (generator.Next(6))
        {
            case 0 when generator.Chance(20):
                return $"(str.in_re {Of(depth)} (str.to_re {Of(depth)}))";
            case 0:
            case 1:
                return $"(str.is_digit {Of(depth)})";
            case 2:
                return $"(str.contains {Of(depth)} {Part(depth)})";
            case 3:
                return $"({generator.Pick(["str.prefixof", "str.suffixof"])} {Part(depth)} {Of(depth)})";
            default:
                return $"({generator.Pick(PREDICATES)} {Of(depth)} {Of(depth)})";
        }
    }

    #endregion
}
