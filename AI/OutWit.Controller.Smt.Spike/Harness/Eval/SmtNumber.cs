namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>
/// An Int or a Real. The value is exact; whether it is of sort Real only matters to the
/// operators that tell the sorts apart (div, mod, to_int, is_int, /). Equality is by value.
/// </summary>
internal sealed class SmtNumber : SmtValue
{
    #region Constructors

    /// <summary>Makes a number.</summary>
    /// <param name="value">The exact value.</param>
    /// <param name="isReal">Whether it is of sort Real.</param>
    public SmtNumber(Rational value, bool isReal)
    {
        Value = value;
        IsReal = isReal;
    }

    #endregion

    #region Functions

    /// <inheritdoc />
    public override bool SameAs(SmtValue other)
    {
        return other is SmtNumber n && n.Value.Equals(Value);
    }

    /// <inheritdoc />
    public override string Key()
    {
        return "n:" + Value;
    }

    #endregion

    #region Properties

    /// <summary>The exact value.</summary>
    public Rational Value { get; }

    /// <summary>Whether the value is of sort Real.</summary>
    public bool IsReal { get; }

    #endregion
}
