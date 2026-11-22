namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// A value of sort Real: an exact rational. A Real that happens to be whole is still a Real;
/// it equals the Int of the same number, as solvers that mix the two sorts have it.
/// </summary>
internal sealed class SmtRealValue : SmtValue
{
    #region Constants

    /// <summary>
    /// The largest rational that may be an index of an array, in machine words. Two rationals
    /// are compared by multiplying across, which costs the product of their sizes: with this
    /// ceiling the comparison of an index with any other costs its own size times the ceiling.
    /// </summary>
    public const int MAX_KEY_WORDS = 64;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the value of a rational.
    /// </summary>
    /// <param name="value">The rational.</param>
    public SmtRealValue(SmtRational value)
    {
        Value = value;
    }

    #endregion

    #region Properties

    /// <inheritdoc />
    public override SmtValueKind Kind => SmtValueKind.Real;

    /// <inheritdoc />
    public override long Weight => 1 + Value.Bits / SmtMeter.WORD_BITS;

    /// <inheritdoc />
    public override long KeyCost => Weight > MAX_KEY_WORDS ? long.MaxValue : Weight * MAX_KEY_WORDS;

    /// <summary>The rational.</summary>
    public SmtRational Value { get; }

    #endregion
}
