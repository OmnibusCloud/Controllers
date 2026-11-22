using System.Numerics;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// A bit-vector: its width and its unsigned value below two to the width.
/// </summary>
internal sealed class SmtBitVectorValue : SmtValue
{
    #region Constructors

    /// <summary>
    /// Creates a bit-vector of a value that is already in range.
    /// </summary>
    /// <param name="value">The unsigned value: not negative, below two to the width.</param>
    /// <param name="width">The width, at least 1.</param>
    public SmtBitVectorValue(BigInteger value, int width)
    {
        Value = value;
        Width = width;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Creates a bit-vector of any integer, reduced modulo two to the width.
    /// </summary>
    /// <param name="value">Any integer.</param>
    /// <param name="width">The width, at least 1.</param>
    /// <returns>The bit-vector.</returns>
    public static SmtBitVectorValue Wrap(BigInteger value, int width)
    {
        var modulus = BigInteger.One << width;
        if (value.Sign < 0 || value >= modulus)
        {
            value %= modulus;
            if (value.Sign < 0)
                value += modulus;
        }

        return new SmtBitVectorValue(value, width);
    }

    #endregion

    #region Properties

    /// <inheritdoc />
    public override SmtValueKind Kind => SmtValueKind.BitVector;

    /// <inheritdoc />
    public override long Weight => 1 + Width / SmtMeter.WORD_BITS;

    /// <summary>The unsigned value.</summary>
    public BigInteger Value { get; }

    /// <summary>The width in bits.</summary>
    public int Width { get; }

    /// <summary>Two to the width, less one: every bit set.</summary>
    public BigInteger Mask => (BigInteger.One << Width) - BigInteger.One;

    /// <summary>Whether the most significant bit is set.</summary>
    public bool IsNegative => !(Value >> (Width - 1)).IsZero;

    /// <summary>The value read as two's complement.</summary>
    public BigInteger Signed => IsNegative ? Value - (BigInteger.One << Width) : Value;

    #endregion
}
