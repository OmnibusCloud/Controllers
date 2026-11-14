namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>A Boolean value.</summary>
internal sealed class SmtBool : SmtValue
{
    #region Constants

    /// <summary>true.</summary>
    public static readonly SmtBool TRUE = new(true);

    /// <summary>false.</summary>
    public static readonly SmtBool FALSE = new(false);

    #endregion

    #region Constructors

    private SmtBool(bool value)
    {
        Value = value;
    }

    #endregion

    #region Functions

    /// <summary>The value of a C# bool.</summary>
    /// <param name="value">The bool.</param>
    /// <returns><see cref="TRUE"/> or <see cref="FALSE"/>.</returns>
    public static SmtBool Of(bool value)
    {
        return value ? TRUE : FALSE;
    }

    /// <inheritdoc />
    public override bool SameAs(SmtValue other)
    {
        return other is SmtBool b && b.Value == Value;
    }

    /// <inheritdoc />
    public override string Key()
    {
        return Value ? "true" : "false";
    }

    #endregion

    #region Properties

    /// <summary>The value.</summary>
    public bool Value { get; }

    #endregion
}
