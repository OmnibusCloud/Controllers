namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>
/// An element of an uninterpreted sort, named as the model names it (Z3: <c>U!val!0</c>). Two
/// elements are equal exactly when their names are: the model's universe lists distinct elements.
/// </summary>
internal sealed class SmtUninterpreted : SmtValue
{
    #region Constructors

    /// <summary>Makes an element.</summary>
    /// <param name="name">The model's name for it.</param>
    public SmtUninterpreted(string name)
    {
        Name = name;
    }

    #endregion

    #region Functions

    /// <inheritdoc />
    public override bool SameAs(SmtValue other)
    {
        return other is SmtUninterpreted u && u.Name == Name;
    }

    /// <inheritdoc />
    public override string Key()
    {
        return "u:" + Name;
    }

    #endregion

    #region Properties

    /// <summary>The model's name for the element.</summary>
    public string Name { get; }

    #endregion
}
