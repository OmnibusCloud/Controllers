using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// A function the evaluator can call: a definition of the problem, the model's interpretation
/// of a symbol the problem declares, a function the model added for itself, or a declared
/// symbol the model left out (no body: every call gives the default value of the result sort).
/// </summary>
internal sealed class SmtFunction
{
    #region Constructors

    /// <summary>
    /// Creates a function.
    /// </summary>
    /// <param name="parameters">The names of the parameters.</param>
    /// <param name="parameterSorts">The sort of each parameter.</param>
    /// <param name="resultSort">The sort of the result; null for a named term, which states none.</param>
    /// <param name="body">The body; null for a symbol the model left out.</param>
    /// <param name="isModel">Whether the body is the model's text, which may use the model's own names.</param>
    public SmtFunction(IReadOnlyList<string> parameters, IReadOnlyList<SmtSort> parameterSorts, SmtSort? resultSort, SmtExpression? body, bool isModel)
    {
        Parameters = parameters;
        ParameterSorts = parameterSorts;
        ResultSort = resultSort;
        Body = body;
        IsModel = isModel;
    }

    #endregion

    #region Properties

    /// <summary>The names of the parameters.</summary>
    public IReadOnlyList<string> Parameters { get; }

    /// <summary>The sort of each parameter.</summary>
    public IReadOnlyList<SmtSort> ParameterSorts { get; }

    /// <summary>The sort of the result; null for a named term, which states none.</summary>
    public SmtSort? ResultSort { get; }

    /// <summary>The body; null for a symbol the model left out.</summary>
    public SmtExpression? Body { get; }

    /// <summary>Whether the body is the model's text, which may use the model's own names.</summary>
    public bool IsModel { get; }

    /// <summary>How many arguments the function takes.</summary>
    public int Arity => ParameterSorts.Count;

    #endregion
}
