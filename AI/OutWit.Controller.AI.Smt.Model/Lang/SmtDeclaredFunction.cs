namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// A function a problem declares by a <c>declare-fun</c> of one argument or more, with its
/// signature: what a model gives an interpretation to. Immutable.
/// </summary>
public sealed class SmtDeclaredFunction
{
    #region Constructors

    /// <summary>
    /// Creates the declared function.
    /// </summary>
    /// <param name="symbol">The function's symbol, as declared.</param>
    /// <param name="parameters">The sorts of its arguments, in order.</param>
    /// <param name="sort">The sort of its value.</param>
    public SmtDeclaredFunction(SmtExpression symbol, IReadOnlyList<SmtExpression> parameters, SmtExpression sort)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(sort);

        Symbol = symbol;
        Parameters = parameters;
        Sort = sort;
    }

    #endregion

    #region Properties

    /// <summary>The function's name, without bars.</summary>
    public string Name => Symbol.Text;

    /// <summary>The function's symbol, as declared.</summary>
    public SmtExpression Symbol { get; }

    /// <summary>The sorts of the function's arguments, in order.</summary>
    public IReadOnlyList<SmtExpression> Parameters { get; }

    /// <summary>The sort of the function's value.</summary>
    public SmtExpression Sort { get; }

    #endregion
}
