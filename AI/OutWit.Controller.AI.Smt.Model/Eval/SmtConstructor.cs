using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// A constructor of a datatype: its name, and for each field the selector's name and the sort
/// as the declaration wrote it.
/// </summary>
internal sealed class SmtConstructor
{
    #region Constructors

    /// <summary>
    /// Creates a constructor.
    /// </summary>
    /// <param name="name">The constructor's name.</param>
    /// <param name="datatype">The datatype it builds.</param>
    /// <param name="ordinal">Its number among the constructors of the datatype.</param>
    /// <param name="selectors">The selector of each field.</param>
    /// <param name="fieldSortExpressions">The sort of each field, as written.</param>
    public SmtConstructor(string name, SmtDatatype datatype, int ordinal, IReadOnlyList<string> selectors, IReadOnlyList<SmtExpression> fieldSortExpressions)
    {
        Name = name;
        Datatype = datatype;
        Ordinal = ordinal;
        Selectors = selectors;
        FieldSortExpressions = fieldSortExpressions;
    }

    #endregion

    #region Properties

    /// <summary>The constructor's name.</summary>
    public string Name { get; }

    /// <summary>The datatype it builds.</summary>
    public SmtDatatype Datatype { get; }

    /// <summary>The constructor's number among the constructors of its datatype.</summary>
    public int Ordinal { get; }

    /// <summary>The selector of each field.</summary>
    public IReadOnlyList<string> Selectors { get; }

    /// <summary>The sort of each field, as the declaration wrote it.</summary>
    public IReadOnlyList<SmtExpression> FieldSortExpressions { get; }

    /// <summary>The sort of each field of a datatype without parameters; null for a datatype with parameters, and until the declaration is read.</summary>
    public IReadOnlyList<SmtSort>? FieldSorts { get; set; }

    /// <summary>How many fields the constructor has.</summary>
    public int Arity => Selectors.Count;

    #endregion
}
