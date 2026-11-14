using System.Numerics;

namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>A bit-vector: its width and its unsigned value below 2^width.</summary>
internal sealed class SmtBitVec : SmtValue
{
    #region Constructors

    /// <summary>Makes a bit-vector, reducing the value modulo 2^width.</summary>
    /// <param name="value">Any integer.</param>
    /// <param name="width">The width, at least 1.</param>
    public SmtBitVec(BigInteger value, int width)
    {
        if (width < 1)
            throw new SmtPartialException("bit-vector of width 0");

        Width = width;
        var modulus = BigInteger.One << width;
        value %= modulus;
        Value = value.Sign < 0 ? value + modulus : value;
    }

    #endregion

    #region Functions

    /// <inheritdoc />
    public override bool SameAs(SmtValue other)
    {
        return other is SmtBitVec b && b.Width == Width && b.Value == Value;
    }

    /// <inheritdoc />
    public override string Key()
    {
        return $"bv{Width}:{Value}";
    }

    #endregion

    #region Properties

    /// <summary>The unsigned value.</summary>
    public BigInteger Value { get; }

    /// <summary>The width in bits.</summary>
    public int Width { get; }

    /// <summary>2^width - 1.</summary>
    public BigInteger Mask => (BigInteger.One << Width) - 1;

    /// <summary>Whether the most significant bit is set.</summary>
    public bool Negative => !(Value >> (Width - 1)).IsZero;

    /// <summary>The two's-complement value.</summary>
    public BigInteger Signed => Negative ? Value - (BigInteger.One << Width) : Value;

    #endregion
}
