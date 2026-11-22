namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// A truth value. There are two, shared by every validation.
/// </summary>
internal sealed class SmtBoolValue : SmtValue
{
    #region Constants

    /// <summary>The value true.</summary>
    public static readonly SmtBoolValue TRUE = new(true);

    /// <summary>The value false.</summary>
    public static readonly SmtBoolValue FALSE = new(false);

    #endregion

    #region Constructors

    private SmtBoolValue(bool value)
    {
        Value = value;
    }

    #endregion

    #region Functions

    /// <summary>
    /// The value of a truth.
    /// </summary>
    /// <param name="value">The truth.</param>
    /// <returns><see cref="TRUE"/> or <see cref="FALSE"/>.</returns>
    public static SmtBoolValue Of(bool value)
    {
        return value ? TRUE : FALSE;
    }

    #endregion

    #region Properties

    /// <inheritdoc />
    public override SmtValueKind Kind => SmtValueKind.Bool;

    /// <summary>The truth.</summary>
    public bool Value { get; }

    #endregion
}
