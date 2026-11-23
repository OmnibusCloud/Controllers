namespace OutWit.Controller.AI.Smt.Tests.Differential;

/// <summary>
/// The productions of one theory: given a sort, a term of that sort whose head is an operator
/// of the theory, or nothing when the theory has none for the sort. Subterms come from the
/// generator, which may hand them to another family.
/// </summary>
internal abstract class SmtTermFamily
{
    #region Constructors

    /// <summary>
    /// Creates the family of one generator.
    /// </summary>
    /// <param name="generator">The generator the subterms come from.</param>
    protected SmtTermFamily(SmtTermGenerator generator)
    {
        Generator = generator;
    }

    #endregion

    #region Functions

    /// <summary>
    /// A term of a sort with an operator of the family at its head.
    /// </summary>
    /// <param name="sort">The sort.</param>
    /// <param name="depth">How many levels the term may still nest; at least 1.</param>
    /// <returns>The term, or null when the family has no operator of that sort.</returns>
    public abstract string? Produce(SmtGenSort sort, int depth);

    /// <summary>
    /// A literal of a sort the family owns: one the generator's own literals do not write.
    /// </summary>
    /// <param name="sort">The sort.</param>
    /// <returns>The literal, or null when the family owns no such sort.</returns>
    public virtual string? Literal(SmtGenSort sort)
    {
        return null;
    }

    /// <summary>
    /// A constant of a sort that the family's prelude declares and its model interprets.
    /// </summary>
    /// <param name="sort">The sort.</param>
    /// <returns>The constant's name, or null when the family declares none of that sort.</returns>
    public virtual string? Constant(SmtGenSort sort)
    {
        return null;
    }

    #endregion

    #region Properties

    /// <summary>The generator the subterms come from.</summary>
    protected SmtTermGenerator Generator { get; }

    #endregion
}
