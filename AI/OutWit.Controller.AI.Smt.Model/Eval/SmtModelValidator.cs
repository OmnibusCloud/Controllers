using System.Globalization;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// Checks a <c>sat</c> answer without trusting whoever gave it: every assertion of the problem
/// is evaluated under the model the solver printed, and every value the solver reported with
/// the model is held to it, by code that shares nothing with a solver.
/// <list type="bullet">
/// <item><see cref="SmtValidationClass.Validated"/>: every assertion evaluated to true, and
/// every reported value is the value its term has under the model. The model, with a default
/// value for each symbol it leaves out, is a witness: the problem is satisfiable whoever said
/// so.</item>
/// <item><see cref="SmtValidationClass.Failed"/>: an assertion evaluated to false, or a
/// reported value is another than its term's, and nothing it rests on was guessed: no symbol
/// the model left out, no construct the evaluator does not compute to the end. The answer is
/// wrong.</item>
/// <item><see cref="SmtValidationClass.Partial"/>: everything else.</item>
/// </list>
/// <para>
/// The model is checked as the model: the assertions are evaluated under the definitions of the
/// answer of <c>get-model</c>, and a value of the answer of <c>get-value</c> never stands in for
/// one of them. That answer is a claim of its own, checked pair by pair
/// (<see cref="SmtReportedValues"/>); a caller that knows which terms were asked says so
/// (<see cref="Validate(SmtModel, IReadOnlyList{SmtExpression}, SmtEvaluationBudget)"/>), and
/// the answer is then held to those terms as well.
/// </para>
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

    private readonly SmtEvaluationProblem m_problem;

    #endregion

    #region Constructors

    /// <summary>
    /// Reads a problem the sanitizer took, once, for any number of models.
    /// </summary>
    /// <param name="problem">The problem as the sanitizer took it: its kept commands are what is evaluated.</param>
    /// <exception cref="ArgumentNullException">The problem is null.</exception>
    public SmtModelValidator(SmtProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        m_problem = SmtEvaluationProblemReader.Read(problem.Commands);
    }

    /// <summary>
    /// Reads a problem from its commands once, for any number of models.
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
        m_problem = SmtEvaluationProblemReader.Read(commands);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Checks one model against the problem, and every value that was read with the model
    /// against the model: each pair of the answer of <c>get-value</c> is held to the term it
    /// names itself. For a caller that does not know which terms were asked; one that knows
    /// says so (<see cref="Validate(SmtModel, IReadOnlyList{SmtExpression}, SmtEvaluationBudget)"/>).
    /// </summary>
    /// <param name="model">What the solver printed.</param>
    /// <param name="budget">What the validation may spend, the assertions and the values together.</param>
    /// <returns>The class, the reason and the counts.</returns>
    /// <exception cref="ArgumentNullException">The model or the budget is null.</exception>
    public SmtValidation Validate(SmtModel model, SmtEvaluationBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(budget);

        return Checked(model, null, budget);
    }

    /// <summary>
    /// Checks one model against the problem, and the values that were read with it against the
    /// terms that were asked and against the model. The answer of <c>get-value</c> must hold as
    /// many pairs as terms were asked, each pair's term written as the term asked at its place
    /// (a solver echoes a term as it was written to it), or it is not the answer of what was
    /// asked: no pair of it is used then, and the validation is partial at best, with
    /// <see cref="SmtValidationReasons.UNREADABLE_VALUES"/>. So is a model without values where
    /// terms were asked, and a model with values where none were.
    /// </summary>
    /// <param name="model">What the solver printed.</param>
    /// <param name="asked">The terms whose values were asked, in the order they were asked; none when no value was asked.</param>
    /// <param name="budget">What the validation may spend, the assertions and the values together.</param>
    /// <returns>The class, the reason and the counts.</returns>
    /// <exception cref="ArgumentNullException">The model, the terms or the budget is null.</exception>
    /// <exception cref="ArgumentException">A term is missing.</exception>
    public SmtValidation Validate(SmtModel model, IReadOnlyList<SmtExpression> asked, SmtEvaluationBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(asked);
        ArgumentNullException.ThrowIfNull(budget);

        // The validation's own list: what the caller does to its list afterwards does not reach it.
        var terms = new SmtExpression[asked.Count];
        for (var index = 0; index < terms.Length; index++)
            terms[index] = asked[index] ?? throw new ArgumentException("a term is missing", nameof(asked));

        return Checked(model, terms, budget);
    }

    private SmtValidation Checked(SmtModel model, IReadOnlyList<SmtExpression>? asked, SmtEvaluationBudget budget)
    {
        if (m_problem.Fault != null)
            return Partial(m_problem.Fault, 0);

        if (!model.DefinitionsReadable)
            return Partial(SmtValidationReasons.UNREADABLE_MODEL, 0);

        try
        {
            return SmtEvaluationThread.Run(() => Guarded(model, asked, budget), budget.MaxDepth);
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
    private SmtValidation Guarded(SmtModel model, IReadOnlyList<SmtExpression>? asked, SmtEvaluationBudget budget)
    {
        try
        {
            return Evaluate(model, asked, budget);
        }
        catch (Exception)
        {
            return Partial(SmtValidationReasons.EVALUATOR_ERROR, 0);
        }
    }

    private SmtValidation Evaluate(SmtModel model, IReadOnlyList<SmtExpression>? asked, SmtEvaluationBudget budget)
    {
        var values = new SmtReportedValues(model, asked);
        var run = new SmtRun(m_problem, model, budget, values.AreTaken);
        var meter = run.Meter;
        var defined = Defined(model, values.AreTaken);
        var truths = 0;
        var falseAt = 0;
        var ended = false;

        // An answer of get-value that is not taken is known before anything is evaluated: it is
        // the first reason met, and the model is still checked as the model.
        var reason = values.AreTaken ? null : SmtValidationReasons.UNREADABLE_VALUES;
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
                {
                    ended = true;
                    break;
                }
            }
        }

        // A false assertion found before anything ended the evaluation stays false: nothing
        // that came after it can make the model satisfy it.
        if (falseAt > 0)
            return Outcome(SmtValidationClass.Failed, SmtValidationReasons.ASSERTION_FALSE, falseAt.ToString(CultureInfo.InvariantCulture), truths, defined, meter);

        // The reported values, after the assertions and from what the budget has left. A value
        // that is another than its term's stays so whatever was left open before it.
        if (values.AreTaken && !ended)
        {
            values.Check(run);
            if (values.MismatchAt > 0)
                return Outcome(SmtValidationClass.Failed, SmtValidationReasons.VALUE_MISMATCH, values.MismatchAt.ToString(CultureInfo.InvariantCulture), truths, defined, meter);

            if (reason == null)
            {
                reason = values.Reason;
                detail = values.Detail;
            }
        }

        return reason != null
            ? Outcome(SmtValidationClass.Partial, reason, detail, truths, defined, meter)
            : Outcome(SmtValidationClass.Validated, null, null, truths, defined, meter);
    }

    private SmtValidation Outcome(SmtValidationClass validation, string? reason, string? detail, int truths, int defined, SmtMeter meter)
    {
        return new SmtValidation
        {
            Class = validation,
            Reason = reason,
            Detail = detail,
            Assertions = m_problem.Assertions.Count,
            True = truths,
            Defined = defined,
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
    /// How many of the symbols the problem declares the model defines: a constant or a function
    /// by a definition of as many parameters, and a constant the definitions leave out by a
    /// reported value, when the reported values are taken.
    /// </summary>
    private int Defined(SmtModel model, bool takesValues)
    {
        var defined = 0;
        foreach (var name in m_problem.Declared)
        {
            var arity = m_problem.Symbols[name].ParameterSorts!.Count;
            if (model.Functions.TryGetValue(name, out var function) ? function.Parameters.Count == arity : arity == 0 && takesValues && model.Completions.ContainsKey(name))
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
