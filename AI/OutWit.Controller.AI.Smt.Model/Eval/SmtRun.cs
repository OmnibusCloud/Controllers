using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// One validation: a problem, a model, a budget, and the parts of the evaluator that work on
/// them. Everything a validation makes (values, sorts met in the model, what it remembers of
/// symbols) lives here and dies with it, so validations share nothing but the problem, which
/// they only read.
/// </summary>
internal sealed class SmtRun
{
    #region Constructors

    /// <summary>
    /// Starts a validation.
    /// </summary>
    /// <param name="problem">The problem.</param>
    /// <param name="model">The model, readable.</param>
    /// <param name="budget">What the validation may spend.</param>
    /// <param name="takesValues">
    /// Whether the answer of <c>get-value</c> that came with the model is taken: false when it
    /// is not the answer of what was asked, and then no value of it completes the model.
    /// </param>
    public SmtRun(SmtEvaluationProblem problem, SmtModel model, SmtEvaluationBudget budget, bool takesValues = true)
    {
        Problem = problem;
        Model = model;
        TakesValues = takesValues;
        Meter = new SmtMeter(budget);
        Sorts = new SmtSortTable(problem.Sorts);

        Conformance = new SmtConformance(this);
        Equality = new SmtEquality(this);
        Arrays = new SmtArrays(this);
        Symbols = new SmtSymbols(this);
        Operators = new SmtOperators(this);
        Evaluator = new SmtEvaluator(this);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Reads a sort as written, in the problem's terms.
    /// </summary>
    /// <param name="sort">The sort as written.</param>
    /// <returns>The sort; a sort without values when the evaluator has none for it.</returns>
    /// <exception cref="SmtPartialException">The step budget is used up.</exception>
    public SmtSort Resolve(SmtExpression sort)
    {
        return Problem.Resolver.Resolve(sort, Sorts, Meter);
    }

    #endregion

    #region Properties

    /// <summary>The problem.</summary>
    public SmtEvaluationProblem Problem { get; }

    /// <summary>The model.</summary>
    public SmtModel Model { get; }

    /// <summary>Whether a value the answer of <c>get-value</c> reports for a constant the model leaves out is that constant's value.</summary>
    public bool TakesValues { get; }

    /// <summary>The validation's accounting.</summary>
    public SmtMeter Meter { get; }

    /// <summary>The sorts of this validation, over the problem's.</summary>
    public SmtSortTable Sorts { get; }

    /// <summary>The walker of terms.</summary>
    public SmtEvaluator Evaluator { get; }

    /// <summary>The names of the problem and of the model.</summary>
    public SmtSymbols Symbols { get; }

    /// <summary>The theory operators.</summary>
    public SmtOperators Operators { get; }

    /// <summary>The check of a value against a declared sort, and the default values of sorts.</summary>
    public SmtConformance Conformance { get; }

    /// <summary>The equality of values.</summary>
    public SmtEquality Equality { get; }

    /// <summary>The arrays.</summary>
    public SmtArrays Arrays { get; }

    #endregion
}
