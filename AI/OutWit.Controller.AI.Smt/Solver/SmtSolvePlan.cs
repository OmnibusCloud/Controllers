using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Model.Problems;
using OutWit.Controller.AI.Smt.Profiles;

namespace OutWit.Controller.AI.Smt.Solver;

/// <summary>
/// One problem as a session takes it: the problem the sanitizer made of the text again, and the
/// terms that stand beside it for its query, each read again too. A problem travels as text, and
/// whatever sent it is not trusted to have looked at it: the solver is given only what this
/// machine's own sanitizer took, and only when the text is the problem it was said to be.
/// </summary>
internal sealed class SmtSolvePlan
{
    #region Constructors

    private SmtSolvePlan(SmtProblem problem, SmtQuery query)
    {
        Problem = problem;
        Query = query;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Makes the plan of a problem, or says why there is none.
    /// </summary>
    /// <param name="data">The problem as it travelled.</param>
    /// <param name="envelope">The session's envelope: the number of models of an enumeration that names none.</param>
    /// <param name="refusal">Why the problem is not taken; null when it is.</param>
    /// <returns>The plan, or null.</returns>
    public static SmtSolvePlan? Prepare(SmtProblemData data, SmtEnvelope envelope, out SmtRefusalData? refusal)
    {
        refusal = null;

        var sanitized = SmtSanitizer.Sanitize(data.Text ?? "");
        if (!sanitized.IsAccepted)
        {
            refusal = sanitized.Refusal;
            return null;
        }

        // The text is the problem it was said to be, or nothing of it runs.
        var problem = sanitized.Problem;
        if (!string.Equals(problem.Hash, data.Hash, StringComparison.Ordinal))
        {
            refusal = Refuse(SmtSolveCodes.HASH_MISMATCH, $"the text hashes to {problem.Hash}, which is not the name the problem came with: it is not the problem it was said to be");
            return null;
        }

        var plan = new SmtSolvePlan(problem, data.Query);
        refusal = data.Query switch
        {
            SmtQuery.Check => plan.TakeValues(data),
            SmtQuery.Enumerate => plan.TakeProjection(data, envelope),
            SmtQuery.Prove => plan.TakeGoal(data),
            _ => Refuse(SmtRefusalCodes.FIELD_INVALID, "'query' must be check, enumerate or prove")
        };

        return refusal == null ? plan : null;
    }

    private SmtRefusalData? TakeValues(SmtProblemData data)
    {
        var texts = data.Values ?? [];
        if (texts.Count > SmtProblemSetLimits.MAX_VALUES)
            return Refuse(SmtRefusalCodes.FIELD_INVALID, $"'values' holds more than the {SmtProblemSetLimits.MAX_VALUES} terms allowed");

        var terms = new List<SmtExpression>(texts.Count);
        for (var index = 0; index < texts.Count; index++)
        {
            var term = SmtSanitizer.SanitizeTerm(texts[index] ?? "");
            if (!term.IsAccepted)
                return In(term.Refusal, $"values[{index}]");

            terms.Add(term.Term);
        }

        Values = terms;
        return null;
    }

    private SmtRefusalData? TakeProjection(SmtProblemData data, SmtEnvelope envelope)
    {
        // The check the host's reader made of the row, made again of what travelled (the model
        // package's SmtProjection): a node refuses no projection the host took, and takes none
        // the host would have refused.
        var refusal = SmtProjection.Take(Problem, data.Project ?? [], out var constants);
        if (refusal != null)
            return refusal;

        var models = data.MaxModels == 0 ? envelope.MaxModels : data.MaxModels;
        if (models < 1 || models > SmtOptionsRules.MAX_MODELS)
            return Refuse(SmtRefusalCodes.FIELD_INVALID, $"'max_models' must be between 1 and {SmtOptionsRules.MAX_MODELS}");

        Projection = [.. constants.Select(constant => constant.Symbol)];
        MaxModels = models;
        return null;
    }

    private SmtRefusalData? TakeGoal(SmtProblemData data)
    {
        if (string.IsNullOrEmpty(data.Goal))
            return Refuse(SmtRefusalCodes.FIELD_MISSING, "'goal' is missing: a proof names what is to be proved");

        var term = SmtSanitizer.SanitizeTerm(data.Goal);
        if (!term.IsAccepted)
            return In(term.Refusal, "goal");

        Goal = term.Term;
        return null;
    }

    #endregion

    #region Tools

    private static SmtRefusalData Refuse(string code, string reason)
    {
        return new SmtRefusalData { Code = code, Reason = reason };
    }

    /// <summary>The refusal of a term, with the field the term stands in said first, as a row's refusal says it.</summary>
    private static SmtRefusalData In(SmtRefusalData refusal, string field)
    {
        refusal.Reason = $"'{field}': {refusal.Reason}";
        return refusal;
    }

    #endregion

    #region Properties

    /// <summary>The problem as this machine's sanitizer took it.</summary>
    public SmtProblem Problem { get; }

    /// <summary>What is asked of the problem.</summary>
    public SmtQuery Query { get; }

    /// <summary>The terms whose values a check asks for; none for a model alone and for the other queries.</summary>
    public IReadOnlyList<SmtExpression> Values { get; private set; } = [];

    /// <summary>The constants the models of an enumeration are told apart by, each as the problem declares it; none for the other queries.</summary>
    public IReadOnlyList<SmtExpression> Projection { get; private set; } = [];

    /// <summary>The most models an enumeration looks for; 0 for the other queries.</summary>
    public int MaxModels { get; private set; }

    /// <summary>The goal of a proof; null for the other queries.</summary>
    public SmtExpression? Goal { get; private set; }

    #endregion
}
