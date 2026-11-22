namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// A constant a problem declares, by <c>declare-const</c> or by a <c>declare-fun</c> of no
/// arguments: what a model gives a value to. Immutable.
/// </summary>
public sealed class SmtDeclaredConstant
{
    #region Constructors

    /// <summary>
    /// Creates the declared constant.
    /// </summary>
    /// <param name="symbol">The constant's symbol, as declared.</param>
    /// <param name="sort">Its sort.</param>
    public SmtDeclaredConstant(SmtExpression symbol, SmtExpression sort)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(sort);

        Symbol = symbol;
        Sort = sort;
    }

    #endregion

    #region Properties

    /// <summary>The constant's name, without bars.</summary>
    public string Name => Symbol.Text;

    /// <summary>The constant's symbol, as declared.</summary>
    public SmtExpression Symbol { get; }

    /// <summary>The constant's sort.</summary>
    public SmtExpression Sort { get; }

    #endregion
}
