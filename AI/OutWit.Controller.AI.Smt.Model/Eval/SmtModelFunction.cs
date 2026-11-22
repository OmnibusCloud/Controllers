using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// One <c>define-fun</c> of a model, as the solver printed it: the interpretation of a symbol
/// the problem declares, or a function the solver added for itself (the function behind an
/// array). The sorts are kept as written: what they are is a question to the problem.
/// </summary>
internal sealed class SmtModelFunction
{
    #region Constructors

    /// <summary>
    /// Creates the record of a definition.
    /// </summary>
    /// <param name="definition">The whole <c>define-fun</c>.</param>
    /// <param name="parameters">The names of the parameters.</param>
    /// <param name="parameterSorts">The sort of each parameter, as written.</param>
    public SmtModelFunction(SmtExpression definition, IReadOnlyList<string> parameters, IReadOnlyList<SmtExpression> parameterSorts)
    {
        Definition = definition;
        Parameters = parameters;
        ParameterSorts = parameterSorts;
    }

    #endregion

    #region Properties

    /// <summary>The whole <c>define-fun</c>: five items.</summary>
    public SmtExpression Definition { get; }

    /// <summary>The function's name.</summary>
    public string Name => Definition.Items[1].Text;

    /// <summary>The names of the parameters.</summary>
    public IReadOnlyList<string> Parameters { get; }

    /// <summary>The sort of each parameter, as written.</summary>
    public IReadOnlyList<SmtExpression> ParameterSorts { get; }

    /// <summary>The sort of the result, as written.</summary>
    public SmtExpression ResultSort => Definition.Items[3];

    /// <summary>The body.</summary>
    public SmtExpression Body => Definition.Items[4];

    #endregion
}
