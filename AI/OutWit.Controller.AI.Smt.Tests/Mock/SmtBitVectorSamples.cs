using System.Globalization;
using System.Numerics;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// Operands for the tables of the bit-vector operators: at each of the widths 1, 7, 8, 64, 65
/// and 256 the values where the operators have their edges (zero, one, all ones, the most
/// negative and the most positive number) in every pairing, and pairs drawn from a generator
/// with a fixed seed. The same pairs every run, on every machine.
/// </summary>
internal static class SmtBitVectorSamples
{
    #region Constants

    private const int SEED = 20261122;
    private const int RANDOM_PAIRS = 40;

    private static readonly int[] WIDTHS = [1, 7, 8, 64, 65, 256];

    #endregion

    #region Functions

    /// <summary>
    /// The pairs of operands: a few hundred, of every width of the list.
    /// </summary>
    /// <returns>The width and two unsigned values below two to the width.</returns>
    public static IReadOnlyList<(int Width, BigInteger Left, BigInteger Right)> Pairs()
    {
        var random = new Random(SEED);
        var pairs = new List<(int, BigInteger, BigInteger)>();
        foreach (var width in WIDTHS)
        {
            var edges = Edges(width);
            foreach (var left in edges)
            {
                foreach (var right in edges)
                    pairs.Add((width, left, right));
            }

            for (var index = 0; index < RANDOM_PAIRS; index++)
            {
                // Every fourth pair has a small second operand: a shift amount or a divisor that matters.
                var right = index % 4 == 0 ? Next(random, width) % (2 * width) : Next(random, width);
                pairs.Add((width, Next(random, width), right));
            }
        }

        return pairs;
    }

    /// <summary>
    /// A bit-vector as a term: <c>(_ bvN w)</c>.
    /// </summary>
    /// <param name="value">The unsigned value.</param>
    /// <param name="width">The width.</param>
    /// <returns>The term.</returns>
    public static string Literal(BigInteger value, int width)
    {
        return string.Create(CultureInfo.InvariantCulture, $"(_ bv{value} {width})");
    }

    /// <summary>
    /// The two's-complement reading of an unsigned value.
    /// </summary>
    /// <param name="value">The unsigned value.</param>
    /// <param name="width">The width.</param>
    /// <returns>The signed number.</returns>
    public static BigInteger Signed(BigInteger value, int width)
    {
        return value >= BigInteger.Pow(2, width - 1) ? value - BigInteger.Pow(2, width) : value;
    }

    /// <summary>
    /// Any integer as the unsigned value of a bit-vector: reduced modulo two to the width.
    /// </summary>
    /// <param name="number">The integer.</param>
    /// <param name="width">The width.</param>
    /// <returns>The unsigned value.</returns>
    public static BigInteger Unsigned(BigInteger number, int width)
    {
        var modulus = BigInteger.Pow(2, width);
        return ((number % modulus) + modulus) % modulus;
    }

    /// <summary>
    /// The bits of a value, the most significant first.
    /// </summary>
    /// <param name="value">The unsigned value.</param>
    /// <param name="width">The width.</param>
    /// <returns>A text of zeros and ones, as long as the width.</returns>
    public static string Bits(BigInteger value, int width)
    {
        var bits = new char[width];
        for (var index = 0; index < width; index++)
            bits[width - 1 - index] = ((value >> index) & BigInteger.One).IsOne ? '1' : '0';

        return new string(bits);
    }

    /// <summary>
    /// The value a text of zeros and ones writes.
    /// </summary>
    /// <param name="bits">The bits, the most significant first.</param>
    /// <returns>The unsigned value.</returns>
    public static BigInteger OfBits(string bits)
    {
        var value = BigInteger.Zero;
        foreach (var bit in bits)
            value = value * 2 + (bit == '1' ? 1 : 0);

        return value;
    }

    private static BigInteger[] Edges(int width)
    {
        var top = BigInteger.Pow(2, width - 1);
        return new[] { BigInteger.Zero, BigInteger.One, top * 2 - 1, top, top - 1 }.Distinct().Where(value => value >= 0).ToArray();
    }

    private static BigInteger Next(Random random, int width)
    {
        var bytes = new byte[width / 8 + 2];
        random.NextBytes(bytes);
        bytes[^1] = 0;
        return new BigInteger(bytes) % BigInteger.Pow(2, width);
    }

    #endregion
}
