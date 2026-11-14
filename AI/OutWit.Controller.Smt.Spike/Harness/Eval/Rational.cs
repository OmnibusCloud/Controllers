using System.Numerics;

namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>An exact rational number: a numerator and a positive denominator in lowest terms.</summary>
internal readonly struct Rational : IComparable<Rational>, IEquatable<Rational>
{
    #region Constructors

    /// <summary>Makes a rational in lowest terms.</summary>
    /// <param name="numerator">The numerator.</param>
    /// <param name="denominator">The denominator, not zero.</param>
    /// <exception cref="DivideByZeroException">The denominator is zero.</exception>
    public Rational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero)
            throw new DivideByZeroException();

        if (denominator.Sign < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
        if (!gcd.IsOne && !gcd.IsZero)
        {
            numerator /= gcd;
            denominator /= gcd;
        }

        Numerator = numerator;
        Denominator = numerator.IsZero ? BigInteger.One : denominator;
    }

    #endregion

    #region Functions

    /// <summary>The rational of an integer.</summary>
    /// <param name="value">The integer.</param>
    /// <returns>value / 1.</returns>
    public static Rational Of(BigInteger value)
    {
        return new Rational(value, BigInteger.One);
    }

    /// <summary>Reads an SMT-LIB decimal (<c>12.50</c>).</summary>
    /// <param name="text">The decimal's text.</param>
    /// <returns>Its exact value.</returns>
    public static Rational ParseDecimal(string text)
    {
        var dot = text.IndexOf('.');
        if (dot < 0)
            return Of(BigInteger.Parse(text));

        var whole = text[..dot];
        var fraction = text[(dot + 1)..];
        var numerator = BigInteger.Parse(whole + fraction);
        return new Rational(numerator, BigInteger.Pow(10, fraction.Length));
    }

    /// <summary>The greatest integer not above this one.</summary>
    /// <returns>floor(this).</returns>
    public BigInteger Floor()
    {
        var quotient = BigInteger.DivRem(Numerator, Denominator, out var remainder);
        return remainder.Sign < 0 ? quotient - 1 : quotient;
    }

    public static Rational operator +(Rational a, Rational b) => new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);

    public static Rational operator -(Rational a, Rational b) => new(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);

    public static Rational operator -(Rational a) => new(-a.Numerator, a.Denominator);

    public static Rational operator *(Rational a, Rational b) => new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);

    public static Rational operator /(Rational a, Rational b) => new(a.Numerator * b.Denominator, a.Denominator * b.Numerator);

    /// <inheritdoc />
    public int CompareTo(Rational other)
    {
        return (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
    }

    /// <inheritdoc />
    public bool Equals(Rational other)
    {
        return Numerator == other.Numerator && Denominator == other.Denominator;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is Rational other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Numerator, Denominator);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return IsInteger ? Numerator.ToString() : $"{Numerator}/{Denominator}";
    }

    #endregion

    #region Properties

    /// <summary>The numerator (carries the sign).</summary>
    public BigInteger Numerator { get; }

    /// <summary>The denominator, positive.</summary>
    public BigInteger Denominator { get; }

    /// <summary>Whether the value is an integer.</summary>
    public bool IsInteger => Denominator.IsOne;

    /// <summary>The sign: -1, 0 or 1.</summary>
    public int Sign => Numerator.Sign;

    #endregion
}
