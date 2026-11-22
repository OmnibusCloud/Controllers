using System.Globalization;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// Checks a <c>sat</c> answer without trusting whoever gave it: every assertion of the problem
/// is evaluated under the model the solver printed, by code that shares nothing with a solver.
/// <list type="bullet">
/// <item><see cref="SmtValidationClass.Validated"/>: every assertion evaluated to true. The
/// model, with a default value for each symbol it leaves out, is a witness: the problem is
/// satisfiable whoever said so.</item>
/// <item><see cref="SmtValidationClass.Failed"/>: an assertion evaluated to false, and nothing
/// it rests on was guessed: no symbol the model left out, no construct the evaluator does not
/// compute to the end. The answer is wrong.</item>
/// <item><see cref="SmtValidationClass.Partial"/>: everything else.</item>
/// </list>
/// <para>
/// A validator is built once for a problem and checks any number of models, from any number
/// of threads at once. A validation never throws for what the problem or the model holds, and
/// never runs past its budget (<see cref="SmtEvaluationBudget"/>).
/// </para>
/// <para>
/// A recursive definition is taken as the program it reads as. A definition that does not end
/// for some argument has no function behind it; the evaluator sees that only where an
/// assertion calls it there, as a budget that ends the validation.
/// </para>
/// </summary>
public sealed class SmtModelValidator
{
    #region Fields

    private readonly SmtProblem m_problem;

    #endregion

    #region Constructors

    /// <summary>
    /// Reads a problem once, for any number of models.
    /// </summary>
    /// <param name="commands">
    /// The commands a sanitized problem keeps, in order: <c>set-logic</c>, <c>set-info</c>,
    /// declarations and definitions of sorts, datatypes and functions, and <c>assert</c>. Any
    /// other command, or one the evaluator cannot make sense of, makes every validation partial.
    /// </param>
    /// <exception cref="ArgumentNullException">The commands are null.</exception>
    public SmtModelValidator(IReadOnlyList<SmtExpression> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        m_problem = SmtProblemReader.Read(commands);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Checks one model against the problem.
    /// </summary>
    /// <param name="model">The model the solver printed.</param>
    /// <param name="budget">What the validation may spend.</param>
    /// <returns>The class, the reason and the counts.</returns>
    /// <exception cref="ArgumentNullException">The model or the budget is null.</exception>
    public SmtValidation Validate(SmtModel model, SmtEvaluationBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(budget);

        if (m_problem.Fault != null)
            return Partial(m_problem.Fault, 0);

        if (!model.IsReadable)
            return Partial(SmtValidationReasons.UNREADABLE_MODEL, 0);

        try
        {
            return SmtEvaluationThread.Run(() => Guarded(model, budget), budget.MaxDepth);
        }
        catch (Exception)
        {
            // The thread could not be made, or could not be waited for.
            return Partial(SmtValidationReasons.EVALUATOR_ERROR, 0);
        }
    }

    /// <summary>
    /// The evaluation with the last line of defence around it: whatever the evaluator did not
    /// account for becomes a partial answer with a reason of its own, which the tests hold to
    /// zero occurrences. It never becomes a verdict, and never takes the process down.
    /// </summary>
    private SmtValidation Guarded(SmtModel model, SmtEvaluationBudget budget)
    {
        try
        {
            return Evaluate(model, budget);
        }
        catch (Exception)
        {
            return Partial(SmtValidationReasons.EVALUATOR_ERROR, 0);
        }
    }

    private SmtValidation Evaluate(SmtModel model, SmtEvaluationBudget budget)
    {
        var run = new SmtRun(m_problem, model, budget);
        var meter = run.Meter;
        var truths = 0;
        var falseAt = 0;
        string? reason = null;
        string? detail = null;

        for (var index = 0; index < m_problem.Assertions.Count; index++)
        {
            // Whether this assertion rests on a symbol the model left out: the count of such
            // readings only grows, also through an exception a connective went on past.
            var before = meter.Completions;
            try
            {
                if (run.Evaluator.Truth(m_problem.Assertions[index], null, 1))
                    truths++;
                else if (meter.Completions != before)
                    reason ??= SmtValidationReasons.COMPLETED_SYMBOL;
                else if (falseAt == 0)
                    falseAt = index + 1;
            }
            catch (SmtPartialException exception)
            {
                if (reason == null)
                {
                    reason = exception.Reason;
                    detail = exception.Detail;
                }

                // A budget ends the validation; so does an exception the budget cannot pay for.
                if (exception.Kind == SmtPartialKind.Terminal || !meter.TryChargeCaught(exception, 0))
                    break;
            }
        }

        // A false assertion found before anything ended the evaluation stays false: nothing
        // that came after it can make the model satisfy it.
        if (falseAt > 0)
            return Outcome(SmtValidationClass.Failed, SmtValidationReasons.ASSERTION_FALSE, falseAt.ToString(CultureInfo.InvariantCulture), truths, model, meter);

        return reason != null
            ? Outcome(SmtValidationClass.Partial, reason, detail, truths, model, meter)
            : Outcome(SmtValidationClass.Validated, null, null, truths, model, meter);
    }

    private SmtValidation Outcome(SmtValidationClass validation, string? reason, string? detail, int truths, SmtModel model, SmtMeter meter)
    {
        return new SmtValidation
        {
            Class = validation,
            Reason = reason,
            Detail = detail,
            Assertions = m_problem.Assertions.Count,
            True = truths,
            Defined = Defined(model),
            Steps = meter.Steps
        };
    }

    private SmtValidation Partial(string reason, long steps)
    {
        return new SmtValidation
        {
            Class = SmtValidationClass.Partial,
            Reason = reason,
            Assertions = m_problem.Assertions.Count,
            Steps = steps
        };
    }

    /// <summary>
    /// How many of the symbols the problem declares the model defines: a constant by a value,
    /// a function by a definition of as many parameters.
    /// </summary>
    private int Defined(SmtModel model)
    {
        var defined = 0;
        foreach (var name in m_problem.Declared)
        {
            var arity = m_problem.Symbols[name].ParameterSorts!.Count;
            if (arity == 0 ? model.TryGetConstant(name, out _) : model.Functions.TryGetValue(name, out var function) && function.Parameters.Count == arity)
                defined++;
        }

        return defined;
    }

    #endregion

    #region Properties

    /// <summary>How many assertions the problem has.</summary>
    public int Assertions => m_problem.Assertions.Count;

    /// <summary>How many symbols the problem declares: what a model has to interpret.</summary>
    public int Declared => m_problem.Declared.Count;

    #endregion
}
