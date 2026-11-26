using System.Globalization;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Profiles;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// The host's check of a <c>sat</c> answer: every model a row gives is evaluated against the
/// problem as the host's own sanitizer took it, by the model evaluator of the model package, which
/// shares nothing with the solver. The counterexample of a proof is checked against the problem
/// and the negated goal.
/// <para>
/// The values a row gives are a claim like its model, never a source. The model is checked as the
/// model, by its own definitions; then the values are held to the terms the row asked the values
/// of, which the host knows from the problem (<see cref="SmtAskedTerms"/>), and to the model: a
/// check's one answer of values to its model, each model of an enumeration with the values of its
/// projected constants. Values that are not the answer of what was asked (unreadable, another
/// number of them, other terms, values where none were asked or none where some were) leave the
/// row partial; a value that is another than its term's under the model fails the row; a value
/// the evaluator does not decide leaves it partial. No values text is dropped unread.
/// </para>
/// <para>
/// A row that says <c>sat</c> but whose model did not fit the session's output is partial (its
/// model is not there to check), and so is one that says <c>sat</c> under a status no session
/// gives a verdict with, or without a model: the intake refuses such rows, and this is the line
/// behind it. The class of a row is the worst of its models: failed when one is failed, partial
/// when one is partial, validated only when all are.
/// </para>
/// <para>
/// <b>What a check may cost.</b> A model's text is read under limits of the reader's own
/// (<see cref="LIMITS"/>): its nesting, numbers, literals and strings at what the evaluator could
/// use at the most, its names at what a problem holds, its tokens above the largest model the
/// controller's sets and kits hold. A text past one is no model the host checks, and is
/// unreadable before it is held. A model is checked
/// under the evaluator's budget, and the models of one result package under the package's
/// (<see cref="SmtPackageBudget"/>): a row whose package has no steps left is not checked further
/// and is partial (<see cref="SmtRunWords.BUDGET_PACKAGE"/>).
/// </para>
/// <para>
/// An evaluator is made once for a problem and checks any number of rows of it. A check never
/// throws for what a row holds: whatever reading or validating a row throws, short of the host
/// running out of memory or of stack, is that row's partial class, and the settlement goes on
/// with the next row. A cancelled job is the job's, never a row's. A check gives the same class
/// for the same row and the same budget left on every host.
/// </para>
/// </summary>
internal sealed class SmtHostEvaluator
{
    #region Constants

    /// <summary>The most characters of a model or of an answer of values the host reads: twice what a session may write in all.</summary>
    public const int MAX_MODEL_CHARACTERS = 2 * SmtProfile.OUTPUT_LIMIT_BYTES;

    /// <summary>
    /// The most tokens of a model or of an answer of values the host reads, every atom and every
    /// parenthesis one: 5 Mi. The largest model of the controller's sets and kits, a solver's
    /// definitions of 15.9 million characters that nearly fill a session's output, has 3,376,297;
    /// the limit is a half above it, and a text of one-letter atoms that fills a session's output
    /// is refused five eighths of the way into it.
    /// </summary>
    public const int MAX_MODEL_TOKENS = 5 * 1024 * 1024;

    /// <summary>
    /// The most digits of a numeral or a decimal of a model: a numeral of more is wider than the
    /// evaluator's integer ceiling (<see cref="SmtEvaluationBudget.DEFAULT_INTEGER_BITS"/>; a
    /// decimal digit is more than 0.301 of a bit).
    /// </summary>
    public const int MAX_MODEL_NUMERAL_DIGITS = SmtEvaluationBudget.DEFAULT_INTEGER_BITS * 31 / 100 + 1;

    /// <summary>
    /// How a model is read: within its characters, its tokens, the evaluator's highest nesting
    /// ceiling (the deepest model of the sets and kits nests 83 lists deep; a term nested deeper
    /// ends a check at that ceiling, and a chain of definitions' conditionals nested deeper, which
    /// the evaluator would walk without nesting, is refused all the same), the evaluator's ceilings
    /// of a number, a bit-vector and a string, and names no longer than the longest a problem can
    /// hold.
    /// </summary>
    public static readonly SmtReaderLimits LIMITS = new()
    {
        MaxCharacters = MAX_MODEL_CHARACTERS,
        MaxTokens = MAX_MODEL_TOKENS,
        MaxDepth = SmtEvaluationBudget.DEPTH_CEILING,
        MaxNumeralDigits = MAX_MODEL_NUMERAL_DIGITS,
        MaxLiteralBits = SmtEvaluationBudget.DEFAULT_BIT_VECTOR_BITS,
        MaxStringCharacters = SmtEvaluationBudget.DEFAULT_STRING_CHARACTERS,
        MaxSymbolCharacters = SmtReaderLimits.PROBLEM_CHARACTERS
    };

    #endregion

    #region Fields

    private readonly SmtProblem m_problem;
    private readonly SmtProblemData m_data;
    private SmtModelValidator? m_validator;
    private IReadOnlyList<SmtExpression>? m_asked;
    private string? m_unchecked;

    #endregion

    #region Constructors

    /// <summary>An evaluator of the rows of one problem.</summary>
    /// <param name="problem">The problem as the host's sanitizer took it.</param>
    /// <param name="data">The problem as it travelled: its query, its goal, and the terms its rows' values answer.</param>
    public SmtHostEvaluator(SmtProblem problem, SmtProblemData data)
    {
        m_problem = problem;
        m_data = data;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Makes the evaluator of a problem from its text as it travelled, sanitized again here.
    /// </summary>
    /// <param name="data">The problem.</param>
    /// <returns>The evaluator, or null when the host's sanitizer does not take the text (no row of it says sat then).</returns>
    public static SmtHostEvaluator? Of(SmtProblemData data)
    {
        var sanitized = SmtSanitizer.Sanitize(data.Text ?? "");
        return sanitized.IsAccepted ? new SmtHostEvaluator(sanitized.Problem, data) : null;
    }

    /// <summary>Checks the models of a row, and the values that came with them, to the row's end: no package's budget.</summary>
    /// <param name="row">The row, of this evaluator's problem.</param>
    /// <returns>The class and its reason.</returns>
    public SmtModelCheck Check(SmtResultData row)
    {
        return Walk(row, null, 0, [], CancellationToken.None).Check;
    }

    /// <summary>
    /// Checks the models of a row under the budget of the package it came in: before every model
    /// the package must have steps left, and every model pays its price to it. What a model gave
    /// is kept in a list of the caller's, which the other copies of the same digest share: a model
    /// is checked once, and every copy's package pays for it.
    /// </summary>
    /// <param name="row">The row, of this evaluator's problem.</param>
    /// <param name="budget">The budget of the row's package; null for none.</param>
    /// <param name="maxSteps">What the package may spend in all.</param>
    /// <param name="outcomes">What the row's models gave so far, in order: read, and added to.</param>
    /// <param name="cancellation">The job's cancellation, looked at before every model.</param>
    /// <returns>The walk: the class and its reason as far as it went, how far, and whether it reached the row's end.</returns>
    /// <exception cref="OperationCanceledException">The job was cancelled.</exception>
    public SmtWalk Walk(SmtResultData row, SmtPackageBudget? budget, long maxSteps, List<SmtModelOutcome> outcomes, CancellationToken cancellation)
    {
        try
        {
            return WalkRow(row, budget, maxSteps, outcomes, cancellation);
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or InsufficientExecutionStackException or OperationCanceledException))
        {
            // One row's doing, whatever it was: the row is not checked, and the rows after it are.
            return new SmtWalk(Partial(SmtValidationReasons.EVALUATOR_ERROR), 0, true);
        }
    }

    #endregion

    #region Tools

    private SmtWalk WalkRow(SmtResultData row, SmtPackageBudget? budget, long maxSteps, List<SmtModelOutcome> outcomes, CancellationToken cancellation)
    {
        if (!string.Equals(row.Verdict, SmtResultWords.SAT, StringComparison.Ordinal))
            return Ended(SmtModelCheck.NONE);

        if (row.Status == SmtResultWords.OUTPUT_EXCEEDED)
            return Ended(Partial(SmtRunWords.OUTPUT_EXCEEDED));

        // A sat under a status no session says sat with, or a sat without a model: what the
        // row holds is no model a session gave, and its word is not taken for it.
        if (row.Status != SmtResultWords.OK || row.Models.Count == 0)
            return Ended(Partial(SmtValidationReasons.UNREADABLE_MODEL));

        if (!Prepare())
            return Ended(Partial(m_unchecked!));

        // A text of values without a model of its own to hold it to is not read as an answer.
        SmtModelCheck? partial = row.Values.Count > row.Models.Count ? Partial(SmtValidationReasons.UNREADABLE_VALUES) : null;
        for (var model = 0; model < row.Models.Count; model++)
        {
            // The package has spent what it may: the rest of the row is not checked, and the
            // first reason met stands.
            if (budget != null && !budget.HasRoom(maxSteps))
                return new SmtWalk(partial ?? Partial(SmtRunWords.BUDGET_PACKAGE), model, false);

            cancellation.ThrowIfCancellationRequested();
            if (model == outcomes.Count)
                outcomes.Add(Evaluate(row, model));

            var outcome = outcomes[model];
            budget?.Charge(outcome.Steps);

            if (outcome.Fault)
                return new SmtWalk(Partial(SmtValidationReasons.EVALUATOR_ERROR), model + 1, true);

            if (outcome.Class == SmtRunWords.FAILED)
                return new SmtWalk(new SmtModelCheck(SmtRunWords.FAILED, outcome.Reason, outcome.Detail), model + 1, true);

            if (outcome.Class == SmtRunWords.PARTIAL)
                partial ??= Partial(outcome.Reason);
        }

        return new SmtWalk(partial ?? new SmtModelCheck(SmtRunWords.VALIDATED, "", ""), row.Models.Count, true);
    }

    /// <summary>Checks one model of a row, with the values beside it, and prices it.</summary>
    private SmtModelOutcome Evaluate(SmtResultData row, int model)
    {
        var values = model < row.Values.Count ? row.Values[model] : null;
        var characters = (long)(row.Models[model]?.Length ?? 0) + (values?.Length ?? 0);
        try
        {
            var validation = m_validator!.Validate(SmtModel.Read(row.Models[model], values, LIMITS), m_asked!, SmtEvaluationBudget.Default);
            var price = SmtPackageBudget.Price(validation, characters);

            if (validation.Class == SmtValidationClass.Failed)
            {
                var detail = row.Models.Count > 1 ? $"model {(model + 1).ToString(CultureInfo.InvariantCulture)}: {validation.Detail}" : validation.Detail ?? "";
                return new SmtModelOutcome(SmtRunWords.FAILED, validation.Reason ?? SmtValidationReasons.ASSERTION_FALSE, detail, price, false);
            }

            return validation.Class == SmtValidationClass.Partial
                ? new SmtModelOutcome(SmtRunWords.PARTIAL, validation.Reason ?? "", "", price, false)
                : new SmtModelOutcome(SmtRunWords.VALIDATED, "", "", price, false);
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or InsufficientExecutionStackException))
        {
            return new SmtModelOutcome(SmtRunWords.PARTIAL, SmtValidationReasons.EVALUATOR_ERROR, "", SmtPackageBudget.Price(null, characters), true);
        }
    }

    /// <summary>
    /// Makes, once, what every row of the problem is checked with: the validator of the problem's
    /// query (for a proof the problem with its negated goal) and the terms a row's values answer.
    /// </summary>
    /// <returns>False when the host cannot make them of what travelled with the problem; <see cref="m_unchecked"/> then says why.</returns>
    private bool Prepare()
    {
        if (m_validator != null)
            return true;

        if (m_unchecked != null)
            return false;

        SmtExpression? negated = null;
        if (m_data.Query == SmtQuery.Prove)
        {
            var goal = SmtSanitizer.SanitizeTerm(m_data.Goal ?? "");
            if (goal.IsAccepted)
                negated = SmtScripts.AssertNot(goal.Term);
        }

        var asked = SmtAskedTerms.Of(m_problem, m_data);
        if (asked == null || m_data.Query == SmtQuery.Prove && negated == null)
        {
            m_unchecked = SmtValidationReasons.ILL_FORMED_TERM;
            return false;
        }

        m_asked = asked;
        m_validator = negated == null ? new SmtModelValidator(m_problem) : new SmtModelValidator([.. m_problem.Commands, negated]);
        return true;
    }

    private static SmtWalk Ended(SmtModelCheck check)
    {
        return new SmtWalk(check, 0, true);
    }

    private static SmtModelCheck Partial(string reason)
    {
        return new SmtModelCheck(SmtRunWords.PARTIAL, reason, "");
    }

    #endregion
}
