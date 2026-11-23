using System.Globalization;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Profiles;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// The host's check of a <c>sat</c> answer: every model a row gives is evaluated against the
/// problem as the host's own sanitizer took it, by the model evaluator of the model package, which
/// shares nothing with the solver. A check's model and the values it asked for are read together
/// (the values hold over the model for the constants they name); every model of an enumeration is
/// checked with its projected values; the counterexample of a proof is checked against the problem
/// and the negated goal. A row that says <c>sat</c> but whose model did not fit the session's output
/// is partial (its model is not there to check). The class of a row is the worst of its models:
/// failed when one is failed, partial when one is partial, validated only when all are.
/// <para>
/// An evaluator is made once for a problem and checks any number of rows of it. A check never
/// throws for what a row holds, and gives the same class for the same row on every host.
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
    private string? m_unchecked;

    #endregion

    #region Constructors

    /// <summary>An evaluator of the rows of one problem.</summary>
    /// <param name="problem">The problem as the host's sanitizer took it.</param>
    /// <param name="data">The problem as it travelled: its query and its goal.</param>
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

    /// <summary>Checks the models of a row.</summary>
    /// <param name="row">The row, of this evaluator's problem.</param>
    /// <returns>The class and its reason.</returns>
    public SmtModelCheck Check(SmtResultData row)
    {
        if (!string.Equals(row.Verdict, SmtResultWords.SAT, StringComparison.Ordinal) || row.Status == SmtResultWords.NODE_FAULT)
            return SmtModelCheck.NONE;

        if (row.Status == SmtResultWords.OUTPUT_EXCEEDED)
            return new SmtModelCheck(SmtRunWords.PARTIAL, SmtRunWords.OUTPUT_EXCEEDED, "");

        if (row.Status != SmtResultWords.OK)
            return SmtModelCheck.NONE;

        if (row.Models.Count == 0)
            return new SmtModelCheck(SmtRunWords.PARTIAL, SmtValidationReasons.UNREADABLE_MODEL, "");

        var validator = Validator();
        if (validator == null)
            return new SmtModelCheck(SmtRunWords.PARTIAL, m_unchecked!, "");

        SmtModelCheck? partial = null;
        for (var model = 0; model < row.Models.Count; model++)
        {
            var values = model < row.Values.Count ? row.Values[model] : null;
            var validation = Validate(validator, row.Models[model], values);

            if (validation.Class == SmtValidationClass.Failed)
            {
                var detail = row.Models.Count > 1 ? $"model {(model + 1).ToString(CultureInfo.InvariantCulture)}: {validation.Detail}" : validation.Detail ?? "";
                return new SmtModelCheck(SmtRunWords.FAILED, validation.Reason ?? SmtValidationReasons.ASSERTION_FALSE, detail);
            }

            if (validation.Class == SmtValidationClass.Partial)
                partial ??= new SmtModelCheck(SmtRunWords.PARTIAL, validation.Reason ?? "", "");
        }

        return partial ?? new SmtModelCheck(SmtRunWords.VALIDATED, "", "");
    }

    #endregion

    #region Tools

    /// <summary>
    /// A model and the values that came with it. A text of values the evaluator cannot read
    /// together with the model (a constant asked for twice) does not hide the model: it is then
    /// checked alone.
    /// </summary>
    private static SmtValidation Validate(SmtModelValidator validator, string model, string? values)
    {
        var validation = validator.Validate(SmtModel.Read(model, values, LIMITS), SmtEvaluationBudget.Default);
        if (values == null || validation.Class != SmtValidationClass.Partial || validation.Reason != SmtValidationReasons.UNREADABLE_MODEL)
            return validation;

        return validator.Validate(SmtModel.Read(model, null, LIMITS), SmtEvaluationBudget.Default);
    }

    /// <summary>The validator of the problem's query, made once: for a proof the problem with its negated goal.</summary>
    private SmtModelValidator? Validator()
    {
        if (m_validator != null || m_unchecked != null)
            return m_validator;

        if (m_data.Query != SmtQuery.Prove)
            return m_validator = new SmtModelValidator(m_problem);

        var goal = SmtSanitizer.SanitizeTerm(m_data.Goal ?? "");
        if (!goal.IsAccepted)
        {
            m_unchecked = SmtValidationReasons.ILL_FORMED_TERM;
            return null;
        }

        return m_validator = new SmtModelValidator([.. m_problem.Commands, SmtScripts.AssertNot(goal.Term)]);
    }

    #endregion
}
