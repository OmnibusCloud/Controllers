namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// An element of a sort the problem leaves to the model, named as the model names it. Two
/// elements of a sort are the same element exactly when they have the same name: the elements
/// a model lists are different from each other.
/// </summary>
internal sealed class SmtElementValue : SmtValue
{
    #region Constructors

    /// <summary>
    /// Creates an element.
    /// </summary>
    /// <param name="name">The model's name for it; empty for the element a sort without elements in the model is given.</param>
    /// <param name="sort">The element's sort.</param>
    public SmtElementValue(string name, SmtSort sort)
    {
        Name = name;
        Sort = sort;
    }

    #endregion

    #region Properties

    /// <inheritdoc />
    public override SmtValueKind Kind => SmtValueKind.Element;

    /// <inheritdoc />
    public override long Weight => 1 + Name.Length / sizeof(long);

    /// <summary>The model's name for the element.</summary>
    public string Name { get; }

    /// <summary>The element's sort.</summary>
    public SmtSort Sort { get; }

    #endregion
}
