using System.Numerics;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// An exact rational number: a numerator that carries the sign and a positive denominator, in
/// lowest terms. Whoever computes with rationals checks their sizes and pays for the work
/// first (<see cref="SmtMeter"/>); nothing here is bounded by itself.
/// </summary>
internal readonly struct SmtRational : IComparable<SmtRational>, IEquatable<SmtRational>
{
    #region Constants

    private const int DECIMAL_BASE = 10;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the rational of a fraction, reduced to lowest terms.
    /// </summary>
    /// <param name="numerator">The numerator.</param>
    /// <param name="denominator">The denominator, not zero.</param>
    /// <exception cref="ArgumentException">The denominator is zero.</exception>
    public SmtRational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero)
            throw new ArgumentException("a rational has no zero denominator", nameof(denominator));

        if (denominator.Sign < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        if (!denominator.IsOne)
        {
            var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
            if (!divisor.IsOne)
            {
                numerator /= divisor;
                denominator /= divisor;
            }
        }

        Numerator = numerator;
        Denominator = denominator;
    }

    /// <summary>
    /// Creates the rational of an integer.
    /// </summary>
    /// <param name="value">The integer.</param>
    public SmtRational(BigInteger value)
    {
        Numerator = value;
        Denominator = BigInteger.One;
    }

    #endregion

    #region Functions

    /// <summary>
    /// The rational of a decimal's digits: the digits of both sides of the dot as one integer,
    /// over ten to the number of digits after it.
    /// </summary>
    /// <param name="digits">Every digit of the decimal, the dot left out.</param>
    /// <param name="fractionDigits">How many of them stood after the dot.</param>
    /// <returns>The exact value.</returns>
    public static SmtRational OfDecimal(BigInteger digits, int fractionDigits)
    {
        return new SmtRational(digits, BigInteger.Pow(DECIMAL_BASE, fractionDigits));
    }

    /// <summary>
    /// The greatest integer that is not above the rational.
    /// </summary>
    /// <returns>The floor.</returns>
    public BigInteger Floor()
    {
        var quotient = BigInteger.DivRem(Numerator, Denominator, out var remainder);
        return remainder.Sign < 0 ? quotient - BigInteger.One : quotient;
    }

    /// <inheritdoc />
    public int CompareTo(SmtRational other)
    {
        if (Denominator.IsOne && other.Denominator.IsOne)
            return Numerator.CompareTo(other.Numerator);

        return (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
    }

    /// <inheritdoc />
    public bool Equals(SmtRational other)
    {
        return Numerator == other.Numerator && Denominator == other.Denominator;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is SmtRational other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Numerator, Denominator);
    }

    /// <summary>The sum of two rationals.</summary>
    /// <param name="left">One rational.</param>
    /// <param name="right">The other.</param>
    /// <returns>Their sum, in lowest terms.</returns>
    public static SmtRational operator +(SmtRational left, SmtRational right)
    {
        return new SmtRational(left.Numerator * right.Denominator + right.Numerator * left.Denominator, left.Denominator * right.Denominator);
    }

    /// <summary>The difference of two rationals.</summary>
    /// <param name="left">One rational.</param>
    /// <param name="right">The other.</param>
    /// <returns>Their difference, in lowest terms.</returns>
    public static SmtRational operator -(SmtRational left, SmtRational right)
    {
        return new SmtRational(left.Numerator * right.Denominator - right.Numerator * left.Denominator, left.Denominator * right.Denominator);
    }

    /// <summary>
    /// The negation of a rational. A fraction in lowest terms is in lowest terms under the
    /// other sign too, so nothing is reduced again: the negation costs nothing of the number's
    /// size, and nobody pays for it.
    /// </summary>
    /// <param name="value">The rational.</param>
    /// <returns>The same number with the other sign.</returns>
    public static SmtRational operator -(SmtRational value)
    {
        return OfLowestTerms(-value.Numerator, value.Denominator);
    }

    /// <summary>The product of two rationals.</summary>
    /// <param name="left">One rational.</param>
    /// <param name="right">The other.</param>
    /// <returns>Their product, in lowest terms.</returns>
    public static SmtRational operator *(SmtRational left, SmtRational right)
    {
        return new SmtRational(left.Numerator * right.Numerator, left.Denominator * right.Denominator);
    }

    /// <summary>The quotient of two rationals; the divisor is not zero.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor, not zero.</param>
    /// <returns>Their quotient, in lowest terms.</returns>
    /// <exception cref="ArgumentException">The divisor is zero.</exception>
    public static SmtRational operator /(SmtRational left, SmtRational right)
    {
        return new SmtRational(left.Numerator * right.Denominator, left.Denominator * right.Numerator);
    }

    /// <summary>
    /// The rational of parts that are known to be in lowest terms, the denominator positive:
    /// taken as they are. The constructor would look for a common divisor that is not there,
    /// at the price of a division of the two.
    /// </summary>
    private static SmtRational OfLowestTerms(BigInteger numerator, BigInteger denominator)
    {
        return new SmtRational { Numerator = numerator, Denominator = denominator };
    }

    #endregion

    #region Properties

    /// <summary>The numerator; it carries the sign.</summary>
    public BigInteger Numerator { get; private init; }

    /// <summary>The denominator: positive, and 1 for an integer.</summary>
    public BigInteger Denominator { get; private init; }

    /// <summary>Whether the rational is an integer.</summary>
    public bool IsInteger => Denominator.IsOne;

    /// <summary>The sign: -1, 0 or 1.</summary>
    public int Sign => Numerator.Sign;

    /// <summary>The bits of the numerator and of the denominator together.</summary>
    public long Bits => Numerator.GetBitLength() + Denominator.GetBitLength();

    #endregion
}
