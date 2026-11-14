namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>
/// A closed value of the evaluator: Bool, a number (Int or Real), a bit-vector, a string, a
/// datatype value, an element of an uninterpreted sort, or an array. Values compare by their
/// content; an array whose content is not known as a finite map cannot be compared and makes
/// the comparison partial.
/// </summary>
internal abstract class SmtValue
{
    #region Functions

    /// <summary>
    /// Whether two values are equal. Throws <see cref="SmtPartialException"/> when it cannot be
    /// decided (an array known only as a function).
    /// </summary>
    /// <param name="other">The other value.</param>
    /// <returns>True when equal.</returns>
    public abstract bool SameAs(SmtValue other);

    /// <summary>A text that two equal values share and two different values do not (map keys).</summary>
    /// <returns>The key.</returns>
    public abstract string Key();

    /// <inheritdoc />
    public override string ToString()
    {
        return Key();
    }

    #endregion
}
