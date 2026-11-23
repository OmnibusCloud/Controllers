using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Differential;

/// <summary>
/// One generated term, evaluated by both: the model evaluator computes its value under the
/// term's model, and the solver is asked whether the term equals that value. The solver gets
/// the closed script of the problem <c>(assert (= term value))</c> and the model: the model's
/// interpretations as definitions, nothing left to choose (the elements of the model's sort are
/// stated to differ, as the evaluator takes them). Its <c>sat</c> is an agreement, its
/// <c>unsat</c> a disagreement, after which it is asked for its own value of the term.
/// <para>
/// Where the evaluator gives no value, the reason is the outcome and the solver is not asked:
/// a term the evaluator leaves open is never a disagreement. The evaluator's value is first
/// checked by the evaluator itself, as the validator checks a model: the assertion that the
/// term equals it must be validated, or the value is a fault of the evaluator or of the
/// writing of it.
/// </para>
/// </summary>
internal static class SmtDifferential
{
    #region Constants

    private static readonly SmtOptionsData OPTIONS = new() { ResourceLimit = 200_000_000, HardCapSeconds = 30 };

    #endregion

    #region Functions

    /// <summary>
    /// Evaluates a term by both.
    /// </summary>
    /// <param name="term">The term.</param>
    /// <returns>What became of it.</returns>
    public static SmtDifferentialOutcome Run(SmtGeneratedTerm term)
    {
        IReadOnlyList<SmtExpression> prelude;
        SmtExpression expression;
        SmtModel model;
        try
        {
            prelude = SmtReader.Read(term.Prelude, SmtReaderLimits.Problem);
            expression = SmtReader.Read(term.Term, SmtReaderLimits.Problem).Single();
            model = SmtModel.Read(term.Model, null, SmtReaderLimits.Unbounded);
        }
        catch (Exception exception)
        {
            return Fault(term, $"the generated text does not read: {exception.Message}");
        }

        var problem = SmtEvaluationProblemReader.Read(prelude);
        if (problem.Fault != null || !model.IsReadable)
            return Fault(term, $"the prelude or the model is not taken: {problem.Fault ?? "unreadable model"}");

        var run = new SmtRun(problem, model, SmtEvaluationBudget.Default);
        SmtValue value;
        try
        {
            value = run.Evaluator.Eval(expression, null, 1);
        }
        catch (SmtPartialException partial)
        {
            var reason = partial.Detail == null ? partial.Reason : $"{partial.Reason}:{partial.Detail}";
            return partial.Reason == SmtValidationReasons.EVALUATOR_ERROR
                ? Fault(term, reason)
                : new SmtDifferentialOutcome { Term = term, Verdict = SmtDifferentialVerdict.Partial, Reason = reason };
        }
        catch (Exception exception)
        {
            return Fault(term, $"the evaluator threw {exception.GetType().Name}: {exception.Message}");
        }

        if (run.Meter.Completions > 0)
            return Fault(term, "the value rests on a symbol the model leaves out");

        var written = SmtValueWriter.Write(value, term.Sort);
        if (written == null)
            return Fault(term, $"the value is a {value.Kind}, not of the sort {term.Sort}");

        var assertion = SmtReader.Read($"(assert (= {term.Term} {written}))", SmtReaderLimits.Problem).Single();
        var commands = prelude.Append(assertion).ToList();
        var validation = new SmtModelValidator(commands).Validate(model, SmtEvaluationBudget.Default);
        if (validation.Class != SmtValidationClass.Validated)
            return Fault(term, $"the evaluator does not validate its own value {written}: {validation.Class} {validation.Reason} {validation.Detail}");

        return Ask(term, commands, model, written);
    }

    /// <summary>
    /// Asks the solver whether the term equals the evaluator's value.
    /// </summary>
    private static SmtDifferentialOutcome Ask(SmtGeneratedTerm term, IReadOnlyList<SmtExpression> commands, SmtModel model, string written)
    {
        var text = Script(term, commands, model);
        var sanitized = SmtSanitizer.Sanitize(text);
        if (sanitized.Problem == null)
            return NotTaken(term, written, $"the closed script is refused: {sanitized.Refusal!.Code} {sanitized.Refusal.Reason}");

        var problem = sanitized.Problem;
        var solved = SmtSolverRig.Solve(new SmtProblemData { Id = Id(term), Query = SmtQuery.Check, Text = problem.Canonical, Hash = problem.Hash }, OPTIONS);
        switch (solved.Words())
        {
            case "ok sat":
                return new SmtDifferentialOutcome { Term = term, Verdict = SmtDifferentialVerdict.Agreed, Value = written };
            case "ok unsat":
                return new SmtDifferentialOutcome { Term = term, Verdict = SmtDifferentialVerdict.Disagreed, Value = written, Detail = $"the solver says {SolverValue(term, commands, model)}" };
            default:
                return NotTaken(term, written, $"the solver did not answer: {solved}");
        }
    }

    /// <summary>
    /// The solver's own value of the term: the closed script of the problem without the
    /// assertion, and the term asked for by <c>get-value</c>.
    /// </summary>
    private static string SolverValue(SmtGeneratedTerm term, IReadOnlyList<SmtExpression> commands, SmtModel model)
    {
        var sanitized = SmtSanitizer.Sanitize(Script(term, commands.Take(commands.Count - 1).ToList(), model));
        if (sanitized.Problem == null)
            return "(the script without the assertion is refused)";

        var problem = sanitized.Problem;
        var solved = SmtSolverRig.Solve(new SmtProblemData { Id = Id(term), Query = SmtQuery.Check, Text = problem.Canonical, Hash = problem.Hash, Values = [term.Term] }, OPTIONS);
        return solved.Values.Count == 1 ? solved.Values[0] : $"(no value: {solved})";
    }

    /// <summary>
    /// The closed script as text: the model's definitions in place of the declarations, the
    /// elements of the model's sort stated to differ, and no <c>(check-sat)</c>, which the
    /// solver's query adds.
    /// </summary>
    private static string Script(SmtGeneratedTerm term, IReadOnlyList<SmtExpression> commands, SmtModel model)
    {
        var script = SmtClosedScript.Build(commands, model);
        var lines = script.Commands.Take(script.Commands.Count - 1).Select(command => command.ToString()).ToList();
        if (term.Elements.Count > 1)
            lines.Add($"(assert (distinct {string.Join(" ", term.Elements)}))");

        return string.Join("\n", lines);
    }

    private static string Id(SmtGeneratedTerm term)
    {
        return $"{term.Theory}-{term.Number}";
    }

    private static SmtDifferentialOutcome Fault(SmtGeneratedTerm term, string detail)
    {
        return new SmtDifferentialOutcome { Term = term, Verdict = SmtDifferentialVerdict.EvaluatorFault, Detail = detail };
    }

    private static SmtDifferentialOutcome NotTaken(SmtGeneratedTerm term, string written, string detail)
    {
        return new SmtDifferentialOutcome { Term = term, Verdict = SmtDifferentialVerdict.NotTaken, Value = written, Detail = detail };
    }

    #endregion
}
