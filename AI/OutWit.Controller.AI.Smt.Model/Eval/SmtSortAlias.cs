using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// A sort a problem defines by <c>define-sort</c>: a name for another sort, with parameters.
/// </summary>
internal sealed class SmtSortAlias
{
    #region Constructors

    /// <summary>
    /// Creates an alias.
    /// </summary>
    /// <param name="ordinal">Its number among the aliases of the problem.</param>
    /// <param name="parameters">The names of its parameters.</param>
    /// <param name="body">The sort it stands for, as written.</param>
    public SmtSortAlias(int ordinal, IReadOnlyList<string> parameters, SmtExpression body)
    {
        Ordinal = ordinal;
        Parameters = parameters;
        Body = body;
    }

    #endregion

    #region Properties

    /// <summary>The alias's number among the aliases of the problem.</summary>
    public int Ordinal { get; }

    /// <summary>The names of the parameters.</summary>
    public IReadOnlyList<string> Parameters { get; }

    /// <summary>The sort the alias stands for, as written.</summary>
    public SmtExpression Body { get; }

    #endregion
}
