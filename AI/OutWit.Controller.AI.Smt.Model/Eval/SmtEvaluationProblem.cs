using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// What a problem says, as the evaluator needs it: the sorts, every name with what it stands
/// for, and the assertions. Read once from the problem's commands and then only read, by any
/// number of validations at once.
/// </summary>
internal sealed class SmtEvaluationProblem
{
    #region Constructors

    /// <summary>
    /// Creates the account of a problem; <see cref="SmtEvaluationProblemReader"/> does.
    /// </summary>
    /// <param name="sorts">The problem's sorts.</param>
    /// <param name="resolver">How a sort as written is read.</param>
    /// <param name="symbols">Every name the problem introduces.</param>
    /// <param name="declared">The names the problem declares, in order: what a model interprets.</param>
    /// <param name="assertions">The asserted terms, in order.</param>
    /// <param name="fault">Why no model of the problem can be checked, or null.</param>
    public SmtEvaluationProblem(SmtSortTable sorts, SmtSortResolver resolver, IReadOnlyDictionary<string, SmtSymbol> symbols,
        IReadOnlyList<string> declared, IReadOnlyList<SmtExpression> assertions, string? fault)
    {
        Sorts = sorts;
        Resolver = resolver;
        Symbols = symbols;
        Declared = declared;
        Assertions = assertions;
        Fault = fault;
    }

    #endregion

    #region Properties

    /// <summary>The problem's sorts.</summary>
    public SmtSortTable Sorts { get; }

    /// <summary>How a sort as written is read.</summary>
    public SmtSortResolver Resolver { get; }

    /// <summary>Every name the problem introduces, with what it stands for.</summary>
    public IReadOnlyDictionary<string, SmtSymbol> Symbols { get; }

    /// <summary>The names the problem declares, in order: what a model interprets.</summary>
    public IReadOnlyList<string> Declared { get; }

    /// <summary>The asserted terms, in order.</summary>
    public IReadOnlyList<SmtExpression> Assertions { get; }

    /// <summary>
    /// Why no model of the problem can be checked: a code of <see cref="SmtValidationReasons"/>
    /// for a command the evaluator cannot make sense of, or for a name with two meanings; null
    /// for a problem it can check.
    /// </summary>
    public string? Fault { get; }

    #endregion
}
