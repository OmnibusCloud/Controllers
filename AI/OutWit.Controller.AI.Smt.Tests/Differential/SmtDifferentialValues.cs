using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Differential;

/// <summary>
/// One generated term as a value a row asks for: the solver is given the term's declarations as
/// a problem and is asked for the term's value with <c>get-value</c>, as a node's session asks;
/// the validator then holds the solver's own answer to the term and to the solver's own model,
/// as the host does with a row. An honest answer must never come out failed.
/// <para>
/// A term over symbols the prelude declares is pinned first: the problem asserts that the term
/// has the value the evaluator gives it under the generated model, so the solver has to build a
/// model of its own for those symbols, and the constants and a few applications of the
/// functions are asked beside the term. A closed term needs no model, and its value is the
/// solver's computation of it against the evaluator's.
/// </para>
/// <para>
/// The same answer is then checked once more with its first value changed into another value of
/// the same sort: what a node writes that solved honestly and reports something else.
/// </para>
/// </summary>
internal static class SmtDifferentialValues
{
    #region Constants

    private const string ITE = "ite";

    private static readonly SmtOptionsData OPTIONS = new() { ResourceLimit = 200_000_000, HardCapSeconds = 30 };

    /// <summary>What is asked beside a term of the functions' prelude: its constants, and applications that reach both of its sorts.</summary>
    private static readonly string[] BESIDE = ["x", "y", "b", "u", "v", "(f x)", "(k u)", "(s v)", "(g x y)"];

    #endregion

    #region Functions

    /// <summary>
    /// Asks the solver for the value of a term and checks its answer.
    /// </summary>
    /// <param name="term">The term.</param>
    /// <returns>What became of it.</returns>
    public static SmtDifferentialValuesOutcome Run(SmtGeneratedTerm term)
    {
        var text = term.Prelude + Pin(term);
        var sanitized = SmtSanitizer.Sanitize(text);
        if (sanitized.Problem == null)
            return NoAnswer(term, $"the problem is refused: {sanitized.Refusal!.Code} {sanitized.Refusal.Reason}");

        var asked = new List<SmtExpression>();
        foreach (var one in term.Model == null ? [term.Term] : BESIDE.Prepend(term.Term))
        {
            var taken = SmtSanitizer.SanitizeTerm(one);
            if (taken.Term == null)
                return NoAnswer(term, $"the term is refused: {taken.Refusal!.Code} {taken.Refusal.Reason}");

            asked.Add(taken.Term);
        }

        var problem = sanitized.Problem;
        var data = new SmtProblemData { Id = $"{term.Theory}-{term.Number}", Query = SmtQuery.Check, Text = problem.Canonical, Hash = problem.Hash, Values = asked.Select(one => one.ToString()).ToList() };
        var solved = SmtSolverRig.Solve(data, OPTIONS);
        if (solved.Words() != "ok sat" || solved.Models.Count != 1 || solved.Values.Count != 1)
            return NoAnswer(term, $"the solver did not answer with a model and values: {solved}");

        var pairs = SmtReader.Read(solved.Values[0], SmtReaderLimits.Unbounded).Single().Items;
        var echoed = pairs.Count == asked.Count && pairs.Select((pair, index) => pair.Items[0].Is(asked[index])).All(same => same);

        var validator = new SmtModelValidator(problem);
        var alone = validator.Validate(SmtModel.Read(solved.Models[0], null, SmtReaderLimits.Unbounded), SmtEvaluationBudget.Default);
        var validation = validator.Validate(SmtModel.Read(solved.Models[0], solved.Values[0], SmtReaderLimits.Unbounded), asked, SmtEvaluationBudget.Default);

        var changed = TryChange(text, solved, pairs, out var other);
        var forged = changed ? validator.Validate(SmtModel.Read(solved.Models[0], other, SmtReaderLimits.Unbounded), asked, SmtEvaluationBudget.Default) : null;

        return new SmtDifferentialValuesOutcome
        {
            Term = term,
            Answered = true,
            Echoed = echoed,
            Alone = alone.Class,
            AloneReason = alone.Reason,
            Class = validation.Class,
            Reason = validation.Reason,
            Detail = validation.Detail,
            Changed = changed,
            ChangedClass = forged?.Class ?? SmtValidationClass.Partial,
            ChangedReason = forged?.Reason,
            Note = $"the solver says {pairs[0].Items[1]}{(changed ? $"; changed: {forged!.Class} {forged.Reason}" : "")}"
        };
    }

    #endregion

    #region Tools

    /// <summary>
    /// The assertion that makes the solver build a model for the symbols a term is over: the
    /// term has the value the evaluator gives it under the generated model, which is therefore
    /// a model of the assertion. Nothing for a closed term, and nothing where the evaluator
    /// gives the term no value it can write.
    /// </summary>
    private static string Pin(SmtGeneratedTerm term)
    {
        if (term.Model == null)
            return "";

        var problem = SmtEvaluationProblemReader.Read(SmtReader.Read(term.Prelude, SmtReaderLimits.Problem));
        var model = SmtModel.Read(term.Model, null, SmtReaderLimits.Unbounded);
        if (problem.Fault != null || !model.IsReadable)
            return "";

        try
        {
            var run = new SmtRun(problem, model, SmtEvaluationBudget.Default);
            var value = run.Evaluator.Eval(SmtReader.Read(term.Term, SmtReaderLimits.Problem).Single(), null, 1);
            var written = run.Meter.Completions > 0 ? null : SmtValueWriter.Write(value, term.Sort);
            return written == null ? "" : $"(assert (= {term.Term} {written}))";
        }
        catch (SmtPartialException)
        {
            return "";
        }
    }

    /// <summary>
    /// The solver's values with the first of them changed into another value of its sort. Not
    /// where the solver left the value as a term it did not decide (an <c>ite</c> over a
    /// comparison of two strings, seen once in six thousand terms): that is no spelling of a
    /// value, and a change to the branch its condition does not pick changes nothing.
    /// </summary>
    private static bool TryChange(string problem, SmtSolved solved, IReadOnlyList<SmtExpression> pairs, out string values)
    {
        values = "";
        var mutants = new SmtModelMutants(new SmtFixture("values", "ALL", problem, solved.Models[0], null));
        if (pairs.Count == 0 || pairs[0].Items[1].IsApplication(ITE) || !mutants.TryChange(pairs[0].Items[1], out var other))
            return false;

        values = SmtExpression.List([SmtExpression.List(pairs[0].Items[0], other), .. pairs.Skip(1)]).ToString();
        return true;
    }

    private static SmtDifferentialValuesOutcome NoAnswer(SmtGeneratedTerm term, string note)
    {
        return new SmtDifferentialValuesOutcome { Term = term, Note = note };
    }

    #endregion
}
