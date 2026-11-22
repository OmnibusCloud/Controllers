using System.Numerics;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// A value of sort Int.
/// </summary>
internal sealed class SmtIntValue : SmtValue
{
    #region Constants

    /// <summary>The integer zero.</summary>
    public static readonly SmtIntValue ZERO = new(BigInteger.Zero);

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the value of an integer.
    /// </summary>
    /// <param name="value">The integer.</param>
    public SmtIntValue(BigInteger value)
    {
        Value = value;
    }

    #endregion

    #region Properties

    /// <inheritdoc />
    public override SmtValueKind Kind => SmtValueKind.Int;

    /// <inheritdoc />
    public override long Weight => 1 + Value.GetBitLength() / SmtMeter.WORD_BITS;

    /// <summary>The integer.</summary>
    public BigInteger Value { get; }

    #endregion
}
