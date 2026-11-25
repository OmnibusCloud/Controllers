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
/// An evaluator is made once for a problem and checks any number of rows of it. A check never
/// throws for what a row holds: whatever reading or validating a row throws, short of the host
/// running out of memory or of stack, is that row's partial class, and the settlement goes on
/// with the next row. A check gives the same class for the same row on every host.
/// </para>
/// </summary>
internal sealed class SmtHostEvaluator
{
    #region Constants

    /// <summary>The most characters of a model or of an answer of values the host reads: twice what a session may write in all.</summary>
    public const int MAX_MODEL_CHARACTERS = 2 * SmtProfile.OUTPUT_LIMIT_BYTES;

    private static readonly SmtReaderLimits LIMITS = new() { MaxCharacters = MAX_MODEL_CHARACTERS };

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

    /// <summary>Checks the models of a row, and the values that came with them.</summary>
    /// <param name="row">The row, of this evaluator's problem.</param>
    /// <returns>The class and its reason.</returns>
    public SmtModelCheck Check(SmtResultData row)
    {
        try
        {
            return CheckRow(row);
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or InsufficientExecutionStackException))
        {
            // One row's doing, whatever it was: the row is not checked, and the rows after it are.
            return Partial(SmtValidationReasons.EVALUATOR_ERROR);
        }
    }

    #endregion

    #region Tools

    private SmtModelCheck CheckRow(SmtResultData row)
    {
        if (!string.Equals(row.Verdict, SmtResultWords.SAT, StringComparison.Ordinal))
            return SmtModelCheck.NONE;

        if (row.Status == SmtResultWords.OUTPUT_EXCEEDED)
            return Partial(SmtRunWords.OUTPUT_EXCEEDED);

        // A sat under a status no session says sat with, or a sat without a model: what the
        // row holds is no model a session gave, and its word is not taken for it.
        if (row.Status != SmtResultWords.OK || row.Models.Count == 0)
            return Partial(SmtValidationReasons.UNREADABLE_MODEL);

        if (!Prepare())
            return Partial(m_unchecked!);

        // A text of values without a model of its own to hold it to is not read as an answer.
        SmtModelCheck? partial = row.Values.Count > row.Models.Count ? Partial(SmtValidationReasons.UNREADABLE_VALUES) : null;
        for (var model = 0; model < row.Models.Count; model++)
        {
            var values = model < row.Values.Count ? row.Values[model] : null;
            var validation = m_validator!.Validate(SmtModel.Read(row.Models[model], values, LIMITS), m_asked!, SmtEvaluationBudget.Default);

            if (validation.Class == SmtValidationClass.Failed)
            {
                var detail = row.Models.Count > 1 ? $"model {(model + 1).ToString(CultureInfo.InvariantCulture)}: {validation.Detail}" : validation.Detail ?? "";
                return new SmtModelCheck(SmtRunWords.FAILED, validation.Reason ?? SmtValidationReasons.ASSERTION_FALSE, detail);
            }

            if (validation.Class == SmtValidationClass.Partial)
                partial ??= Partial(validation.Reason ?? "");
        }

        return partial ?? new SmtModelCheck(SmtRunWords.VALIDATED, "", "");
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

    private static SmtModelCheck Partial(string reason)
    {
        return new SmtModelCheck(SmtRunWords.PARTIAL, reason, "");
    }

    #endregion
}
