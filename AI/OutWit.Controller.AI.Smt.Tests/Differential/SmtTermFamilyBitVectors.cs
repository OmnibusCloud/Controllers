using System.Globalization;
using System.Numerics;
using System.Text;

namespace OutWit.Controller.AI.Smt.Tests.Differential;

/// <summary>
/// Bit-vectors: every operator the evaluator computes, at the widths of <see cref="WIDTHS"/>
/// and at the widths an extraction, a concatenation or an extension passes through. Shifts and
/// rotations by more than the width, division and remainder by zero, the most negative value.
/// </summary>
internal sealed class SmtTermFamilyBitVectors : SmtTermFamily
{
    #region Constants

    /// <summary>The widths a term of the theory is generated at.</summary>
    public static readonly int[] WIDTHS = [1, 7, 8, 16, 32, 64, 65, 128];

    private static readonly string[] FOLDS = ["bvand", "bvor", "bvxor", "bvadd", "bvmul"];
    private static readonly string[] PAIRS = ["bvnand", "bvnor", "bvxnor", "bvsub"];
    private static readonly string[] DIVISIONS = ["bvudiv", "bvurem", "bvsdiv", "bvsrem", "bvsmod"];
    private static readonly string[] SHIFTS = ["bvshl", "bvlshr", "bvashr"];
    private static readonly string[] COMPARISONS = ["bvult", "bvule", "bvugt", "bvuge", "bvslt", "bvsle", "bvsgt", "bvsge"];
    private static readonly string[] TO_INTEGER = ["bv2nat", "ubv_to_int", "sbv_to_int"];
    private static readonly string[] OF_INTEGER = ["int2bv", "int_to_bv"];

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the family.
    /// </summary>
    /// <param name="generator">The generator the subterms come from.</param>
    public SmtTermFamilyBitVectors(SmtTermGenerator generator)
        : base(generator)
    {
    }

    #endregion

    #region Functions

    /// <inheritdoc />
    public override string? Produce(SmtGenSort sort, int depth)
    {
        var generator = Generator;
        switch (sort.Kind)
        {
            case SmtGenSortKind.BitVector:
                return Vector(sort.Width, depth - 1);
            case SmtGenSortKind.Bool:
                var width = generator.Pick(WIDTHS);
                return $"({generator.Pick(COMPARISONS)} {Of(width, depth - 1)} {Of(width, depth - 1)})";
            case SmtGenSortKind.Int:
                return $"({generator.Pick(TO_INTEGER)} {Of(generator.Pick(WIDTHS), depth - 1)})";
            default:
                return null;
        }
    }

    /// <inheritdoc />
    public override string? Literal(SmtGenSort sort)
    {
        return sort.Kind == SmtGenSortKind.BitVector ? Literal(sort, Generator.Edge(sort.Width)) : null;
    }

    /// <summary>
    /// A literal of a value, in one of the three spellings: binary, hexadecimal when the width
    /// allows it, or <c>(_ bvN w)</c>.
    /// </summary>
    /// <param name="sort">A bit-vector sort.</param>
    /// <param name="value">The unsigned value, in range.</param>
    /// <returns>The literal.</returns>
    public string Literal(SmtGenSort sort, BigInteger value)
    {
        var width = sort.Width;
        var roll = Generator.Next(3);
        if (roll == 0 && width % 4 == 0)
            return "#x" + Digits(value, width / 4, 16);

        return roll == 1 ? "#b" + Digits(value, width, 2) : $"(_ bv{value.ToString(CultureInfo.InvariantCulture)} {width})";
    }

    private string Of(int width, int depth)
    {
        return Generator.Term(SmtGenSort.BitVector(width), depth);
    }

    private string Vector(int width, int depth)
    {
        var generator = Generator;
        var sort = SmtGenSort.BitVector(width);
        switch (generator.Next(16))
        {
            case 0:
                return $"({generator.Pick(["bvnot", "bvneg"])} {Of(width, depth)})";
            case 1:
            case 2:
                return $"({generator.Pick(FOLDS)} {generator.Terms(sort, depth, 2, 3)})";
            case 3:
                return $"({generator.Pick(PAIRS)} {Of(width, depth)} {Of(width, depth)})";
            case 4:
            case 5:
                return $"({generator.Pick(DIVISIONS)} {Of(width, depth)} {Divisor(width, depth)})";
            case 6:
                return $"({generator.Pick(SHIFTS)} {Of(width, depth)} {Shift(width, depth)})";
            case 7:
                return $"((_ {generator.Pick(["rotate_left", "rotate_right"])} {Rotation(width)}) {Of(width, depth)})";
            case 8:
                var wider = width + generator.Pick([0, 1, 3, 8, 57]);
                var low = generator.Between(0, wider - width);
                return $"((_ extract {low + width - 1} {low}) {Of(wider, depth)})";
            case 9 when width >= 2:
                var left = generator.Between(1, width - 1);
                return $"(concat {Of(left, depth)} {Of(width - left, depth)})";
            case 10:
                var extension = generator.Between(0, width - 1);
                return $"((_ {generator.Pick(["zero_extend", "sign_extend"])} {extension}) {Of(width - extension, depth)})";
            case 11:
                var divisors = Enumerable.Range(1, width).Where(count => width % count == 0).ToList();
                var times = generator.Pick(divisors);
                return $"((_ repeat {times}) {Of(width / times, depth)})";
            case 12 when width == 1:
                var compared = generator.Pick(WIDTHS);
                return $"(bvcomp {Of(compared, depth)} {Of(compared, depth)})";
            case 13:
                return $"((_ {generator.Pick(OF_INTEGER)} {width}) {generator.Term(SmtGenSort.INT, depth)})";
            default:
                return $"({generator.Pick(FOLDS)} {Of(width, depth)} {Of(width, depth)})";
        }
    }

    /// <summary>A divisor: a term, zero, or one of the edges.</summary>
    private string Divisor(int width, int depth)
    {
        var generator = Generator;
        var sort = SmtGenSort.BitVector(width);
        return generator.Next(3) switch
        {
            0 => Literal(sort, BigInteger.Zero),
            1 => Literal(sort, generator.Edge(width)),
            _ => Of(width, depth)
        };
    }

    /// <summary>A shift: a term, or a distance at or past the width.</summary>
    private string Shift(int width, int depth)
    {
        var generator = Generator;
        var sort = SmtGenSort.BitVector(width);
        var mask = (BigInteger.One << width) - 1;
        return generator.Next(4) switch
        {
            0 => Literal(sort, new BigInteger(width) & mask),
            1 => Literal(sort, new BigInteger(width + 1) & mask),
            2 => Literal(sort, new BigInteger(generator.Between(0, width)) & mask),
            _ => Of(width, depth)
        };
    }

    private int Rotation(int width)
    {
        var generator = Generator;
        return generator.Next(5) switch
        {
            0 => 0,
            1 => width,
            2 => width + 1,
            3 => 2 * width + generator.Between(0, 3),
            _ => generator.Between(0, width)
        };
    }

    private static string Digits(BigInteger value, int count, int radix)
    {
        var builder = new StringBuilder(count);
        var bits = radix == 16 ? 4 : 1;
        for (var index = count - 1; index >= 0; index--)
        {
            var digit = (int)((value >> (index * bits)) & (radix - 1));
            builder.Append("0123456789abcdef"[digit]);
        }

        return builder.ToString();
    }

    #endregion
}
