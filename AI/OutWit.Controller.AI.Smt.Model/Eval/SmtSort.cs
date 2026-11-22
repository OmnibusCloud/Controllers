namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// A sort, resolved: aliases expanded, parameters replaced. A table holds one object for each
/// sort (<see cref="SmtSortTable"/>), so two sorts of one validation are the same sort exactly
/// when they are the same object. Immutable.
/// </summary>
internal sealed class SmtSort
{
    #region Constants

    private static readonly SmtSort[] NO_ARGUMENTS = [];

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a sort. Only a table does: it keeps one object for each sort.
    /// </summary>
    /// <param name="id">The sort's number in its table.</param>
    /// <param name="kind">What the sort is.</param>
    public SmtSort(int id, SmtSortKind kind)
    {
        Id = id;
        Kind = kind;
    }

    #endregion

    #region Properties

    /// <summary>The sort's number: unique among the sorts one validation sees.</summary>
    public int Id { get; }

    /// <summary>What the sort is.</summary>
    public SmtSortKind Kind { get; }

    /// <summary>A bit-vector's width; 0 for any other sort.</summary>
    public int Width { get; init; }

    /// <summary>An array's index sort; null for any other sort.</summary>
    public SmtSort? Index { get; init; }

    /// <summary>An array's element sort; null for any other sort.</summary>
    public SmtSort? Element { get; init; }

    /// <summary>A datatype sort's declaration; null for any other sort.</summary>
    public SmtDatatype? Datatype { get; init; }

    /// <summary>The number of the declaration of a sort the problem leaves to the model; -1 for any other sort.</summary>
    public int Declaration { get; init; } = -1;

    /// <summary>The sort arguments of a datatype or of a declared sort.</summary>
    public IReadOnlyList<SmtSort> Arguments { get; init; } = NO_ARGUMENTS;

    /// <summary>Why the evaluator has no values for the sort: a code of <see cref="SmtValidationReasons"/>; null for a sort it has values for.</summary>
    public string? Reason { get; init; }

    #endregion
}
